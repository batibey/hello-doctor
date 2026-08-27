using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Controllers;

// Hekim doğrulama işlemleri. Yönetici yetkisi yalnızca veritabanından verilir;
// kayıt akışıyla elde edilemez.
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public AdminController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<bool> IsAdminAsync() =>
        await _db.Users.AnyAsync(u => u.Id == CurrentUserId && u.IsAdministrator);

    public record PendingDoctorDto(string Id, string FullName, string Email, string? Specialty,
        string? Title, string? MedicalLicenseNumber, string RegisteredAt, string Verification);

    [HttpGet("doctors/pending")]
    public async Task<IActionResult> Pending()
    {
        if (!await IsAdminAsync()) return Forbid();

        var list = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Doctor && u.Verification == DoctorVerification.Pending)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new PendingDoctorDto(u.Id, u.FullName, u.Email, u.Specialty, u.Title,
                u.MedicalLicenseNumber, u.CreatedAt.ToString("o"), u.Verification.ToString()))
            .ToListAsync();

        return Ok(list);
    }

    [HttpPost("doctors/{id}/verify")]
    public async Task<IActionResult> Verify(string id, [FromBody] VerifyDoctorRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();

        var doctor = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Doctor);
        if (doctor is null) return NotFound();

        var decision = (req.Decision ?? "").Trim().ToLowerInvariant();
        if (decision is not ("verified" or "rejected"))
            return BadRequest(new { message = "Karar 'verified' veya 'rejected' olmalı." });

        // Ret gerekçesi zorunlu: hekim neden reddedildiğini bilmeli ve karar
        // sonradan denetlenebilmeli.
        if (decision == "rejected" && string.IsNullOrWhiteSpace(req.Note))
            return BadRequest(new { message = "Ret gerekçesi zorunlu." });

        doctor.Verification = decision == "verified"
            ? DoctorVerification.Verified
            : DoctorVerification.Rejected;
        doctor.VerifiedAt = DateTime.UtcNow;
        doctor.VerifiedBy = CurrentUserId;
        doctor.VerificationNote = req.Note?.Trim();

        _audit.Record(CurrentUserId, doctor.Id, AuditService.DoctorVerified, decision);
        await _db.SaveChangesAsync();

        return Ok(new { doctor.Id, Verification = doctor.Verification.ToString(), doctor.VerificationNote });
    }

    public record AccessLogDto(string ActorId, string SubjectId, string Action,
        string? ResourceId, string At, string? ClientIp);

    // Denetim kaydı görüntüleme. Kullanıcı kendi kaydına erişenleri görebilir;
    // yönetici tümünü görebilir.
    [HttpGet("access-log")]
    public async Task<IActionResult> AccessLog([FromQuery] string? subjectId, [FromQuery] int take = 100)
    {
        var uid = CurrentUserId;
        var admin = await IsAdminAsync();
        var target = subjectId ?? uid;

        if (!admin && target != uid) return Forbid();

        var list = await _db.AccessLogs.AsNoTracking()
            .Where(x => x.SubjectId == target)
            .OrderByDescending(x => x.At)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new AccessLogDto(x.ActorId, x.SubjectId, x.Action, x.ResourceId,
                x.At.ToString("o"), x.ClientIp))
            .ToListAsync();

        return Ok(list);
    }
}
