using System.Globalization;
using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppointmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AppointmentRules _rules;

    public AppointmentsController(AppDbContext db, AppointmentRules rules)
    {
        _db = db;
        _rules = rules;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private UserRole CurrentRole => Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!);

    public record AppointmentDto(string Id, string PatientId, string PatientName, string DoctorId,
        string DoctorName, string? DoctorSpecialty, string ScheduledAt, string Type, string Status, string? Reason);

    private static AppointmentDto ToDto(Appointment a) => new(
        a.Id, a.PatientId, a.Patient?.FullName ?? "?", a.DoctorId, a.Doctor?.FullName ?? "?",
        a.Doctor?.Specialty, a.ScheduledAt.ToString("o"), a.Type.ToString(), a.Status.ToString(), a.Reason);

    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        var uid = CurrentUserId;
        var list = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Where(a => a.PatientId == uid || a.DoctorId == uid)
            .OrderBy(a => a.ScheduledAt)
            .ToListAsync();
        return Ok(list.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequest req)
    {
        if (CurrentRole != UserRole.Patient)
            return Forbid();

        var doctor = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.DoctorId && u.Role == UserRole.Doctor);
        if (doctor is null)
            return BadRequest(new { message = "Doktor bulunamadı." });
        if (!Enum.TryParse<AppointmentType>(req.Type, true, out var type))
            return BadRequest(new { message = "Geçersiz randevu tipi." });
        if (!DateTime.TryParse(req.ScheduledAt, null,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal, out var when))
            return BadRequest(new { message = "Geçersiz tarih." });

        // Npgsql maps DateTime to timestamptz and rejects non-UTC values.
        var start = DateTime.SpecifyKind(when, DateTimeKind.Utc);

        if (_rules.Validate(start, DateTime.UtcNow) is { } problem)
            return BadRequest(new { message = problem });

        if (await HasConflictAsync(req.DoctorId, CurrentUserId, start))
            return Conflict(new { message = "Bu saat dolu. Başka bir saat seçin." });

        var appt = new Appointment
        {
            PatientId = CurrentUserId,
            DoctorId = req.DoctorId,
            ScheduledAt = start,
            Type = type,
            Reason = req.Reason,
            Status = AppointmentStatus.Pending,
        };
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync();

        await _db.Entry(appt).Reference(a => a.Patient).LoadAsync();
        await _db.Entry(appt).Reference(a => a.Doctor).LoadAsync();
        return Ok(ToDto(appt));
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateAppointmentStatusRequest req)
    {
        var appt = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (appt is null) return NotFound();
        if (appt.PatientId != CurrentUserId && appt.DoctorId != CurrentUserId) return Forbid();
        if (!Enum.TryParse<AppointmentStatus>(req.Status, true, out var status))
            return BadRequest(new { message = "Geçersiz durum." });

        // Katılımcı olmak yetmez: onay doktorun, iptal iki tarafın.
        if (!AppointmentRules.CanTransition(CurrentRole, appt.Status, status))
            return BadRequest(new { message = AppointmentRules.TransitionError(CurrentRole, appt.Status, status) });

        appt.Status = status;
        await _db.SaveChangesAsync();
        return Ok(ToDto(appt));
    }

    // Aynı slot uzunluğu kullanıldığı için çakışma, başlangıçların bir slot
    // aralığında olmasıyla eşdeğer — bu haliyle veritabanında sorgulanabiliyor.
    private async Task<bool> HasConflictAsync(string doctorId, string patientId, DateTime startUtc)
    {
        var slot = _rules.Slot;
        var windowStart = startUtc - slot;
        var windowEnd = startUtc + slot;

        return await _db.Appointments.AnyAsync(a =>
            a.Status != AppointmentStatus.Cancelled
            && a.ScheduledAt > windowStart
            && a.ScheduledAt < windowEnd
            // Doktorun o saati dolu olabilir ya da hasta aynı saate iki randevu alıyordur.
            && (a.DoctorId == doctorId || a.PatientId == patientId));
    }
}
