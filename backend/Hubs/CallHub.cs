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

    public CallHub(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    private string Uid => Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public override Task OnConnectedAsync()
    {
        var set = Connections.GetOrAdd(Uid, _ => new HashSet<string>());
        lock (set) set.Add(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Connections.TryGetValue(Uid, out var set))
        {
            lock (set) set.Remove(Context.ConnectionId);
        }
        return base.OnDisconnectedAsync(exception);
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
    public async Task SendMessage(string recipientId, string text)
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
            };
            db.Messages.Add(msg);
            await db.SaveChangesAsync();
        }

        var payload = new
        {
            id = msg.Id, senderId = msg.SenderId, recipientId = msg.RecipientId,
            text = msg.Text, sentAt = msg.SentAt.ToString("o"), read = false
        };
        await Clients.Clients(ConnectionsOf(recipientId)).SendAsync("ReceiveMessage", payload);
        await Clients.Caller.SendAsync("MessageSent", payload);
    }

    public async Task Typing(string recipientId, bool isTyping)
    {
        await Clients.Clients(ConnectionsOf(recipientId)).SendAsync("Typing", Uid, isTyping);
    }

    // ---- WebRTC signaling ----
    // callType: "voice" | "video"
    public async Task CallUser(string targetId, string callType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == Uid);

        await Clients.Clients(ConnectionsOf(targetId)).SendAsync("IncomingCall", new
        {
            fromId = Uid,
            fromName = caller?.FullName ?? "Bilinmeyen",
            fromColor = caller?.AvatarColor ?? "#4F46E5",
            callType
        });
    }

    public async Task SendOffer(string targetId, object offer)
        => await Clients.Clients(ConnectionsOf(targetId)).SendAsync("ReceiveOffer", Uid, offer);

    public async Task SendAnswer(string targetId, object answer)
        => await Clients.Clients(ConnectionsOf(targetId)).SendAsync("ReceiveAnswer", Uid, answer);

    public async Task SendIceCandidate(string targetId, object candidate)
        => await Clients.Clients(ConnectionsOf(targetId)).SendAsync("ReceiveIceCandidate", Uid, candidate);

    public async Task AcceptCall(string targetId)
        => await Clients.Clients(ConnectionsOf(targetId)).SendAsync("CallAccepted", Uid);

    public async Task RejectCall(string targetId)
        => await Clients.Clients(ConnectionsOf(targetId)).SendAsync("CallRejected", Uid);

    public async Task EndCall(string targetId)
        => await Clients.Clients(ConnectionsOf(targetId)).SendAsync("CallEnded", Uid);
}
