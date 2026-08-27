using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Hubs;

// Handles both real-time chat delivery and WebRTC signaling (offer/answer/ICE)
// for voice & video calls between doctors and patients.
//
// A Hub is transient but resolved from the root provider, so a scoped DbContext
// cannot be injected here — we create one per operation via IDbContextFactory.
//
// Durum süreç belleğinde tutulmuyor: bağlantı listesini SignalR'ın kendisi
// tutuyor (Clients.User), varlık ve görüşme eşleşmesi ise ICallStateStore'da.
// Redis yapılandırılmışsa ikisi de örnekler arasında paylaşılır.
[Authorize]
public class CallHub : Hub
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICallStateStore _state;

    public CallHub(IDbContextFactory<AppDbContext> dbFactory, ICallStateStore state)
    {
        _dbFactory = dbFactory;
        _state = state;
    }

    private string Uid => Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public override async Task OnConnectedAsync()
    {
        await _state.AddConnectionAsync(Uid, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var uid = Uid;
        var lastConnection = await _state.RemoveConnectionAsync(uid, Context.ConnectionId);

        // Son sekme de kapandıysa görüşme fiilen bitmiştir. Karşı tarafa haber
        // vermezsek "Bağlanıyor…" ekranında zaman aşımını beklerdi.
        if (lastConnection)
        {
            var peerId = await _state.GetPeerAsync(uid);
            if (peerId is not null)
            {
                await _state.ClearPairAsync(uid, peerId);
                await Clients.User(peerId).SendAsync("CallEnded", uid);
            }
        }

        await base.OnDisconnectedAsync(exception);
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
        await Clients.User(recipientId).SendAsync("ReceiveMessage", payload);
        await Clients.Caller.SendAsync("MessageSent", payload);
    }

    public async Task Typing(string recipientId, bool isTyping)
    {
        await Clients.User(recipientId).SendAsync("Typing", Uid, isTyping);
    }

    // ---- WebRTC signaling ----
    // Sinyal mesajları yalnızca CallUser ile kurulmuş bir çift arasında iletilir.
    // Aksi halde kullanıcı ID'sini bilen herkes başkasının görüşmesine teklif
    // gönderebilir ya da EndCall ile görüşmeyi düşürebilirdi.
    private async Task<bool> ArePairedAsync(string a, string b) =>
        await _state.GetPeerAsync(a) == b;

    // callType: "voice" | "video"
    // Dönüş: { ok, reason } — reason: "self" | "offline" | "busy".
    // Ulaşılamayan hedefte sessizce başarılı dönmek, arayanı 45 saniyelik zil
    // zaman aşımına kadar boşuna bekletiyordu.
    public async Task<object> CallUser(string targetId, string callType)
    {
        var uid = Uid;
        if (targetId == uid)
            return new { ok = false, reason = "self" };
        if (!await _state.IsOnlineAsync(targetId))
            return new { ok = false, reason = "offline" };

        // Hedefi atomik olarak sahiplen. Kayıt zaten bize aitse arayan tekrar
        // deniyordur, sorun yok; başkasına aitse hedef meşgul demektir.
        if (await _state.ClaimPeerAsync(targetId, uid) != uid)
            return new { ok = false, reason = "busy" };

        await _state.SetPeerAsync(uid, targetId);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);

        await Clients.User(targetId).SendAsync("IncomingCall", new
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
        if (!await ArePairedAsync(Uid, targetId)) return;
        await Clients.User(targetId).SendAsync("ReceiveOffer", Uid, offer);
    }

    public async Task SendAnswer(string targetId, object answer)
    {
        if (!await ArePairedAsync(Uid, targetId)) return;
        await Clients.User(targetId).SendAsync("ReceiveAnswer", Uid, answer);
    }

    public async Task SendIceCandidate(string targetId, object candidate)
    {
        if (!await ArePairedAsync(Uid, targetId)) return;
        await Clients.User(targetId).SendAsync("ReceiveIceCandidate", Uid, candidate);
    }

    public async Task AcceptCall(string targetId)
    {
        if (!await ArePairedAsync(Uid, targetId)) return;
        await Clients.User(targetId).SendAsync("CallAccepted", Uid);
    }

    // reason: "busy" | "error" | null (kullanıcı reddetti)
    public async Task RejectCall(string targetId, string? reason)
    {
        var uid = Uid;
        if (!await ArePairedAsync(uid, targetId)) return;
        await _state.ClearPairAsync(uid, targetId);
        await Clients.User(targetId).SendAsync("CallRejected", uid, reason);
    }

    public async Task EndCall(string targetId)
    {
        var uid = Uid;
        if (!await ArePairedAsync(uid, targetId)) return;
        await _state.ClearPairAsync(uid, targetId);
        await Clients.User(targetId).SendAsync("CallEnded", uid);
    }
}
