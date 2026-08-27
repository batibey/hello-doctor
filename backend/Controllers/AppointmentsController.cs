using System.Globalization;
using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelloDoctor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppointmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AppointmentOptions _options;
    private readonly TimeZoneInfo _tz;

    public AppointmentsController(AppDbContext db, IOptions<AppointmentOptions> options,
        ILogger<AppointmentsController> logger)
    {
        _db = db;
        _options = options.Value;

        try
        {
            _tz = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        }
        catch (Exception ex)
        {
            // Saat dilimi bulunamazsa çalışma saati kontrolü UTC'ye göre yapılır;
            // sessizce yanlış saatleri kabul etmektense loglayıp devam ediyoruz.
            logger.LogWarning(ex, "Saat dilimi {TimeZone} bulunamadı, UTC kullanılıyor.", _options.TimeZone);
            _tz = TimeZoneInfo.Utc;
        }
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

        if (Validate(start) is { } problem)
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
        if (!CanTransition(CurrentRole, appt.Status, status))
            return BadRequest(new { message = TransitionError(CurrentRole, appt.Status, status) });

        appt.Status = status;
        await _db.SaveChangesAsync();
        return Ok(ToDto(appt));
    }

    // ---- Kurallar ----

    // Onay yalnızca doktorda; iptal her iki tarafta. Tamamlandı işaretlemek
    // klinik bir kayıt olduğu için doktorun.
    private static bool CanTransition(UserRole role, AppointmentStatus from, AppointmentStatus to) =>
        (from, to) switch
        {
            // Sonlanmış randevular değişmez.
            (AppointmentStatus.Completed, _) => false,
            (AppointmentStatus.Cancelled, _) => false,

            (AppointmentStatus.Pending, AppointmentStatus.Confirmed) => role == UserRole.Doctor,
            (AppointmentStatus.Confirmed, AppointmentStatus.Completed) => role == UserRole.Doctor,

            (_, AppointmentStatus.Cancelled) => true,

            _ => false,
        };

    private static string TransitionError(UserRole role, AppointmentStatus from, AppointmentStatus to) =>
        (from, to) switch
        {
            (AppointmentStatus.Completed, _) => "Tamamlanmış randevu değiştirilemez.",
            (AppointmentStatus.Cancelled, _) => "İptal edilmiş randevu değiştirilemez.",
            (AppointmentStatus.Pending, AppointmentStatus.Confirmed) when role == UserRole.Patient
                => "Randevuyu yalnızca doktor onaylayabilir.",
            (_, AppointmentStatus.Completed) when role == UserRole.Patient
                => "Randevuyu yalnızca doktor tamamlandı olarak işaretleyebilir.",
            (AppointmentStatus.Pending, AppointmentStatus.Completed)
                => "Randevu önce onaylanmalı.",
            _ => "Bu durum değişikliği yapılamaz.",
        };

    // Geçerliyse null, değilse sebebi döner.
    private string? Validate(DateTime startUtc)
    {
        var earliest = DateTime.UtcNow.AddMinutes(_options.MinimumNoticeMinutes);
        if (startUtc < earliest)
            return startUtc < DateTime.UtcNow
                ? "Geçmiş bir tarihe randevu alınamaz."
                : $"Randevu en az {_options.MinimumNoticeMinutes} dakika sonrası için alınabilir.";

        var local = TimeZoneInfo.ConvertTimeFromUtc(startUtc, _tz);

        if (!_options.WorkingDays.Contains((int)local.DayOfWeek))
            return "Seçilen gün çalışma günü değil.";

        // Randevunun tamamı çalışma saatlerine sığmalı.
        var startMinutes = local.Hour * 60 + local.Minute;
        var endMinutes = startMinutes + _options.SlotMinutes;
        if (startMinutes < _options.WorkingHourStart * 60 || endMinutes > _options.WorkingHourEnd * 60)
            return $"Randevu saatleri {_options.WorkingHourStart:00}:00 - {_options.WorkingHourEnd:00}:00 arasında.";

        return null;
    }

    // Aynı slot uzunluğu kullanıldığı için çakışma, başlangıçların bir slot
    // aralığında olmasıyla eşdeğer — bu haliyle veritabanında sorgulanabiliyor.
    private async Task<bool> HasConflictAsync(string doctorId, string patientId, DateTime startUtc)
    {
        var slot = TimeSpan.FromMinutes(_options.SlotMinutes);
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
