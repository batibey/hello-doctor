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
public class AppointmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AppointmentsController(AppDbContext db) => _db = db;

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
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeLocal,
                out var when))
            return BadRequest(new { message = "Geçersiz tarih." });

        var appt = new Appointment
        {
            PatientId = CurrentUserId,
            DoctorId = req.DoctorId,
            // Npgsql maps DateTime to timestamptz and rejects non-UTC values.
            ScheduledAt = DateTime.SpecifyKind(when, DateTimeKind.Utc),
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

        appt.Status = status;
        await _db.SaveChangesAsync();
        return Ok(ToDto(appt));
    }
}
