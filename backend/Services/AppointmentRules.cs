using HelloDoctor.Api.Models;

namespace HelloDoctor.Api.Services;

// Randevu iş kuralları. Controller'dan ayrı duruyor ki HTTP katmanı olmadan
// sınanabilsin; "şu an"ı parametre olarak alması da bunun için — DateTime.UtcNow
// gömülü olsaydı çalışma saati ve geçmiş tarih kuralları test edilemezdi.
public class AppointmentRules
{
    private readonly AppointmentOptions _options;
    private readonly TimeZoneInfo _timeZone;

    public AppointmentRules(AppointmentOptions options, TimeZoneInfo timeZone)
    {
        _options = options;
        _timeZone = timeZone;
    }

    // Saat dilimi bulunamazsa UTC'ye düşer. Sessizce yanlış saatleri kabul
    // etmektense loglayıp devam etmeyi tercih ediyoruz.
    public static AppointmentRules Create(AppointmentOptions options, ILogger? logger = null)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Saat dilimi {TimeZone} bulunamadı, UTC kullanılıyor.", options.TimeZone);
            tz = TimeZoneInfo.Utc;
        }
        return new AppointmentRules(options, tz);
    }

    public TimeSpan Slot => TimeSpan.FromMinutes(_options.SlotMinutes);

    // Onay yalnızca doktorda; iptal her iki tarafta. Tamamlandı işaretlemek
    // klinik bir kayıt olduğu için doktorun.
    public static bool CanTransition(UserRole role, AppointmentStatus from, AppointmentStatus to) =>
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

    public static string TransitionError(UserRole role, AppointmentStatus from, AppointmentStatus to) =>
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
    public string? Validate(DateTime startUtc, DateTime nowUtc)
    {
        var earliest = nowUtc.AddMinutes(_options.MinimumNoticeMinutes);
        if (startUtc < earliest)
            return startUtc < nowUtc
                ? "Geçmiş bir tarihe randevu alınamaz."
                : $"Randevu en az {_options.MinimumNoticeMinutes} dakika sonrası için alınabilir.";

        var local = TimeZoneInfo.ConvertTimeFromUtc(startUtc, _timeZone);

        if (!_options.WorkingDays.Contains((int)local.DayOfWeek))
            return "Seçilen gün çalışma günü değil.";

        // Randevunun tamamı çalışma saatlerine sığmalı.
        var startMinutes = local.Hour * 60 + local.Minute;
        var endMinutes = startMinutes + _options.SlotMinutes;
        if (startMinutes < _options.WorkingHourStart * 60 || endMinutes > _options.WorkingHourEnd * 60)
            return $"Randevu saatleri {_options.WorkingHourStart:00}:00 - {_options.WorkingHourEnd:00}:00 arasında.";

        return null;
    }
}
