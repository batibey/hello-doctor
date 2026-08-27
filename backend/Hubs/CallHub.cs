using System.Collections.Concurrent;
using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Hubs;

// Handles both real-time chat delivery and WebRTC signaling (offer/answer/ICE)
// for voice & video calls between doctors and patients.
//
// A Hub is transient but resolved from the root provider, so a scoped DbContext
// cannot be injected here — we create one per operation via IDbContextFactory.
[Authorize]
public class CallHub : Hub
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    // userId -> set of connectionIds (a user may be on multiple tabs/devices)
    private static readonly ConcurrentDictionary<string, HashSet<string>> Connections = new();

    // Kim kiminle görüşüyor: userId -> peerId, iki yönlü tutulur. Sinyal
    // mesajları yalnızca burada eşleşen çiftler arasında iletilir — aksi halde
    // bir kullanıcı ID'sini bilen herkes başkasının görüşmesine teklif
    // gönderebilir ya da EndCall ile görüşmeyi düşürebilirdi.
    private static readonly ConcurrentDictionary<string, string> CallPeers = new();

    public CallHub(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    private string Uid => Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public override Task OnConnectedAsync()
    {
        var set = Connections.GetOrAdd(Uid, _ => new HashSet<string>());
        lock (set) set.Add(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var uid = Uid;
        var stillOnline = false;
        if (Connections.TryGetValue(uid, out var set))
        {
            lock (set)
            {
                set.Remove(Context.ConnectionId);
                stillOnline = set.Count > 0;
            }
        }

        // Son sekme de kapandıysa görüşme fiilen bitmiştir. Karşı tarafa haber
        // vermezsek "Bağlanıyor…" ekranında zaman aşımını beklerdi.
        if (!stillOnline && CallPeers.TryRemove(uid, out var peerId))
        {
            CallPeers.TryRemove(new KeyValuePair<string, string>(peerId, uid));
            await Clients.Clients(ConnectionsOf(peerId)).SendAsync("CallEnded", uid);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static IReadOnlyList<string> ConnectionsOf(string userId)
    {
        if (Connections.TryGetValue(userId, out var set))
        {
            lock (set) return set.ToList();
        }
        return Array.Empty<string>();
    }

    // ---- Chat ----
    // text şifreliyse base64 AES-GCM çıktısıdır; sunucu içeriğini göremez,
    // yalnızca taşır ve saklar.
    public async Task SendMessage(string recipientId, string text, bool encrypted,
        string? iv, string? keyForSender, string? keyForRecipient)
    {
        var uid = Uid;
        if (string.IsNullOrWhiteSpace(text)) return;

        ChatMessage msg;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            if (!await db.Users.AnyAsync(u => u.Id == recipientId)) return;

            msg = new ChatMessage
            {
                ConversationId = AppDbContext.ConversationId(uid, recipientId),
                SenderId = uid,
                RecipientId = recipientId,
                Text = text,
                Encrypted = encrypted,
                Iv = iv,
                KeyForSender = keyForSender,
                KeyForRecipient = keyForRecipient,
            };
            db.Messages.Add(msg);
            await db.SaveChangesAsync();
        }

        var payload = new
        {
            id = msg.Id, senderId = msg.SenderId, recipientId = msg.RecipientId,
            text = msg.Text, sentAt = msg.SentAt.ToString("o"), read = false,
            encrypted = msg.Encrypted, iv = msg.Iv,
            keyForSender = msg.KeyForSender, keyForRecipient = msg.KeyForRecipient
        };
        await Clients.Clients(ConnectionsOf(recipientId)).SendAsync("ReceiveMessage", payload);
        await Clients.Caller.SendAsync("MessageSent", payload);
    }

    public async Task Typing(string recipientId, bool isTyping)
    {
        await Clients.Clients(ConnectionsOf(recipientId)).SendAsync("Typing", Uid, isTyping);
    }

    // ---- WebRTC signaling ----
    private static bool ArePaired(string a, string b) =>
        CallPeers.TryGetValue(a, out var peer) && peer == b;

    // Yalnızca beklenen değeri silen kaldırma: araya yeni bir görüşme girmişse
    // eski tarafın gecikmiş EndCall'u onu bozmaz.
    private static void ClearPair(string a, string b)
    {
        CallPeers.TryRemove(new KeyValuePair<string, string>(a, b));
        CallPeers.TryRemove(new KeyValuePair<string, string>(b, a));
    }

    // callType: "voice" | "video"
    // Dönüş: { ok, reason } — reason: "self" | "offline" | "busy".
    // Ulaşılamayan hedefte sessizce başarılı dönmek, arayanı 45 saniyelik zil
    // zaman aşımına kadar boşuna bekletiyordu.
    public async Task<object> CallUser(string targetId, string callType)
    {
        var uid = Uid;
        if (targetId == uid)
            return new { ok = false, reason = "self" };
        if (ConnectionsOf(targetId).Count == 0)
            return new { ok = false, reason = "offline" };

        // Hedefi atomik olarak sahiplen. Kayıt zaten bize aitse arayan tekrar
        // deniyordur, sorun yok; başkasına aitse hedef meşgul demektir.
        if (CallPeers.GetOrAdd(targetId, uid) != uid)
            return new { ok = false, reason = "busy" };
        // Arayan tarafta bayat bir kayıt kalmışsa (tarayıcı çöktü, EndCall
        // gitmedi) sonraki aramaları kalıcı engellemesin diye üzerine yazılır.
        CallPeers[uid] = targetId;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);

        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("IncomingCall", new
        {
            fromId = uid,
            fromName = caller?.FullName ?? "Bilinmeyen",
            fromColor = caller?.AvatarColor ?? "#4F46E5",
            callType
        });
        return new { ok = true, reason = (string?)null };
    }

    public async Task SendOffer(string targetId, object offer)
    {
        if (!ArePaired(Uid, targetId)) return;
        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("ReceiveOffer", Uid, offer);
    }

    public async Task SendAnswer(string targetId, object answer)
    {
        if (!ArePaired(Uid, targetId)) return;
        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("ReceiveAnswer", Uid, answer);
    }

    public async Task SendIceCandidate(string targetId, object candidate)
    {
        if (!ArePaired(Uid, targetId)) return;
        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("ReceiveIceCandidate", Uid, candidate);
    }

    public async Task AcceptCall(string targetId)
    {
        if (!ArePaired(Uid, targetId)) return;
        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("CallAccepted", Uid);
    }

    // reason: "busy" | "error" | null (kullanıcı reddetti)
    public async Task RejectCall(string targetId, string? reason)
    {
        var uid = Uid;
        if (!ArePaired(uid, targetId)) return;
        ClearPair(uid, targetId);
        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("CallRejected", uid, reason);
    }

    public async Task EndCall(string targetId)
    {
        var uid = Uid;
        if (!ArePaired(uid, targetId)) return;
        ClearPair(uid, targetId);
        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("CallEnded", uid);
    }
}
