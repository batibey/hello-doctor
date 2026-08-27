using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext _db;
    public MessagesController(AppDbContext db) => _db = db;

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Sunucu şifreli metni çözemez; şifre çözme için gereken malzemeyi olduğu
    // gibi taşır ve istemci açar.
    public record MessageDto(string Id, string SenderId, string RecipientId, string Text,
        string SentAt, bool Read, bool Encrypted, string? Iv, string? KeyForSender, string? KeyForRecipient);

    // LastMessage da şifreli gelebildiği için önizleme sunucuda üretilemez;
    // sohbet listesindeki son mesaj istemcide çözülür.
    public record ConversationDto(string UserId, string FullName, string Role, string AvatarColor,
        string? Specialty, MessageDto LastMessage, string LastAt, int Unread);

    private static MessageDto ToDto(ChatMessage m) =>
        new(m.Id, m.SenderId, m.RecipientId, m.Text, m.SentAt.ToString("o"), m.Read,
            m.Encrypted, m.Iv, m.KeyForSender, m.KeyForRecipient);

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations()
    {
        var uid = CurrentUserId;

        var mine = await _db.Messages
            .AsNoTracking()
            .Where(m => m.SenderId == uid || m.RecipientId == uid)
            .ToListAsync();

        var partnerIds = mine
            .Select(m => m.SenderId == uid ? m.RecipientId : m.SenderId)
            .Distinct()
            .ToList();

        var partners = await _db.Users
            .AsNoTracking()
            .Where(u => partnerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var result = mine
            .GroupBy(m => m.SenderId == uid ? m.RecipientId : m.SenderId)
            .Select(g =>
            {
                var last = g.OrderByDescending(m => m.SentAt).First();
                partners.TryGetValue(g.Key, out var p);
                return new ConversationDto(
                    g.Key, p?.FullName ?? "?", p?.Role.ToString() ?? "",
                    p?.AvatarColor ?? "#4F46E5", p?.Specialty,
                    ToDto(last), last.SentAt.ToString("o"),
                    g.Count(m => m.RecipientId == uid && !m.Read));
            })
            .OrderByDescending(c => c.LastAt)
            .ToList();

        return Ok(result);
    }

    [HttpGet("{otherUserId}")]
    public async Task<IActionResult> Thread(string otherUserId)
    {
        var uid = CurrentUserId;
        var conv = AppDbContext.ConversationId(uid, otherUserId);

        var msgs = await _db.Messages
            .Where(m => m.ConversationId == conv)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        var unread = msgs.Where(m => m.RecipientId == uid && !m.Read).ToList();
        if (unread.Count > 0)
        {
            foreach (var m in unread) m.Read = true;
            await _db.SaveChangesAsync();
        }

        return Ok(msgs.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest req)
    {
        var uid = CurrentUserId;
        if (!await _db.Users.AnyAsync(u => u.Id == req.RecipientId))
            return BadRequest(new { message = "Alıcı bulunamadı." });

        var msg = new ChatMessage
        {
            ConversationId = AppDbContext.ConversationId(uid, req.RecipientId),
            SenderId = uid,
            RecipientId = req.RecipientId,
            Text = req.Text,
            Encrypted = req.Encrypted,
            Iv = req.Iv,
            KeyForSender = req.KeyForSender,
            KeyForRecipient = req.KeyForRecipient,
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();
        return Ok(ToDto(msg));
    }
}
