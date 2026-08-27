using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelloDoctor.Api.Controllers;

// KVKK veri sahibi hakları: erişim (dışa aktarma), silme ve rıza kaydı.
[ApiController]
[Route("api/privacy")]
[Authorize]
public class PrivacyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ComplianceOptions _options;

    public PrivacyController(AppDbContext db, AuditService audit, IOptions<ComplianceOptions> options)
    {
        _db = db;
        _audit = audit;
        _options = options.Value;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    // Yürürlükteki metin sürümleri; istemci hangi sürümü göstereceğini bilsin.
    [HttpGet("documents")]
    [AllowAnonymous]
    public IActionResult Documents() => Ok(new
    {
        privacyNotice = new { key = "aydinlatma", version = _options.PrivacyNoticeVersion },
        healthDataConsent = new { key = "acik-riza", version = _options.HealthDataConsentVersion },
        emergencyNumber = _options.EmergencyNumber,
    });

    [HttpGet("consents")]
    public async Task<IActionResult> Consents()
    {
        var uid = CurrentUserId;
        var list = await _db.ConsentRecords.AsNoTracking()
            .Where(c => c.UserId == uid)
            .OrderByDescending(c => c.At)
            .Select(c => new ConsentStatusDto(c.DocumentKey, c.DocumentVersion, c.Granted, c.At.ToString("o")))
            .ToListAsync();
        return Ok(list);
    }

    // Rıza geri alınabilir olmalı; KVKK açık rızanın her zaman geri alınabilmesini
    // istiyor. Geri alma da bir kayıt olarak yazılıyor, eski kayıt silinmiyor.
    [HttpPost("consents/{documentKey}")]
    public async Task<IActionResult> SetConsent(string documentKey, [FromBody] bool granted)
    {
        var version = documentKey switch
        {
            "aydinlatma" => _options.PrivacyNoticeVersion,
            "acik-riza" => _options.HealthDataConsentVersion,
            _ => null,
        };
        if (version is null) return BadRequest(new { message = "Bilinmeyen belge." });

        _db.ConsentRecords.Add(new ConsentRecord
        {
            UserId = CurrentUserId,
            DocumentKey = documentKey,
            DocumentVersion = version,
            Granted = granted,
            ClientIp = ClientIp,
        });
        await _db.SaveChangesAsync();

        return Ok(new ConsentStatusDto(documentKey, version, granted, DateTime.UtcNow.ToString("o")));
    }

    // KVKK md. 11: veri sahibi işlenen verilerine erişme hakkına sahip.
    //
    // Mesaj içerikleri şifreli olarak veriliyor — sunucu zaten çözemiyor.
    // İstemci kendi anahtarıyla çözüp okunabilir hale getirebilir.
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var uid = CurrentUserId;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return NotFound();

        var messages = await _db.Messages.AsNoTracking()
            .Where(m => m.SenderId == uid || m.RecipientId == uid)
            .OrderBy(m => m.SentAt)
            .Select(m => new
            {
                m.Id, m.SenderId, m.RecipientId, SentAt = m.SentAt.ToString("o"), m.Read,
                m.Encrypted, m.Text, m.Iv, m.KeyForSender, m.KeyForRecipient,
            })
            .ToListAsync();

        var appointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == uid || a.DoctorId == uid)
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new
            {
                a.Id, a.PatientId, a.DoctorId, ScheduledAt = a.ScheduledAt.ToString("o"),
                Type = a.Type.ToString(), Status = a.Status.ToString(), a.Reason,
            })
            .ToListAsync();

        var consents = await _db.ConsentRecords.AsNoTracking()
            .Where(c => c.UserId == uid)
            .Select(c => new { c.DocumentKey, c.DocumentVersion, c.Granted, At = c.At.ToString("o") })
            .ToListAsync();

        var accessedByOthers = await _db.AccessLogs.AsNoTracking()
            .Where(x => x.SubjectId == uid)
            .OrderByDescending(x => x.At)
            .Select(x => new { x.ActorId, x.Action, At = x.At.ToString("o") })
            .ToListAsync();

        _audit.Record(uid, uid, AuditService.DataExported);
        await _audit.SaveAsync();

        var payload = new
        {
            olusturulma = DateTime.UtcNow.ToString("o"),
            aciklama = "KVKK md. 11 kapsamında veri dışa aktarımı. Şifreli mesaj içerikleri " +
                       "sunucuda çözülemediği için şifreli haliyle verilmiştir.",
            kullanici = new
            {
                user.Id, user.Email, user.FullName, Role = user.Role.ToString(),
                user.Specialty, user.Title, user.Age, user.BloodType,
                KayitTarihi = user.CreatedAt.ToString("o"),
                Dogrulama = user.Verification.ToString(),
            },
            mesajlar = messages,
            randevular = appointments,
            rizalar = consents,
            verimeErisenler = accessedByOthers,
        };

        Response.Headers.ContentDisposition =
            $"attachment; filename=hellodoctor-verilerim-{DateTime.UtcNow:yyyyMMdd}.json";
        return Ok(payload);
    }

    public record DeleteAccountRequest(string Confirmation);

    // KVKK md. 7: silme hakkı.
    //
    // Mesajlar ve randevular yabancı anahtarla korunuyor (silme Restrict) ve
    // karşı tarafın da kaydı olduğu için doğrudan silinemiyor. Bunun yerine
    // hesap anonimleştiriliyor ve şifreleme anahtarı yok ediliyor: anahtar
    // gidince mesaj içeriği hiç kimse tarafından çözülemez hale geliyor, yani
    // içerik fiilen imha oluyor.
    [HttpPost("delete-account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest req)
    {
        if (req.Confirmation != "HESABIMI SİL")
            return BadRequest(new { message = "Onay metni hatalı. 'HESABIMI SİL' yazın." });

        var uid = CurrentUserId;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return NotFound();

        // Bekleyen randevular iptal edilmeli; karşı taraf boşuna beklemesin.
        var upcoming = await _db.Appointments
            .Where(a => (a.PatientId == uid || a.DoctorId == uid)
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Completed)
            .ToListAsync();
        foreach (var a in upcoming) a.Status = AppointmentStatus.Cancelled;

        var silinen = $"silinmis-{Guid.NewGuid():N}";
        user.Email = $"{silinen}@silinmis.local";
        user.FullName = "Silinmiş kullanıcı";
        user.PasswordHash = "";
        user.Bio = null;
        user.Specialty = null;
        user.Title = null;
        user.Age = null;
        user.BloodType = null;
        user.MedicalLicenseNumber = null;
        user.VerificationNote = null;

        // Anahtarların yok edilmesi içeriğin imhası anlamına geliyor.
        user.PublicKey = null;
        user.WrappedPrivateKey = null;
        user.KeyWrapSalt = null;
        user.KeyWrapIv = null;

        _db.PasswordResetTokens.RemoveRange(_db.PasswordResetTokens.Where(t => t.UserId == uid));

        _audit.Record(uid, uid, AuditService.AccountDeleted);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Hesabınız silindi. Şifreleme anahtarınız yok edildiği için " +
                      "mesaj içerikleriniz artık hiç kimse tarafından okunamaz.",
            iptalEdilenRandevu = upcoming.Count,
        });
    }
}
