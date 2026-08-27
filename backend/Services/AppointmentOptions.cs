namespace HelloDoctor.Api.Services;

public class AppointmentOptions
{
    public const string SectionName = "Appointments";

    // Bir randevunun kapladığı süre. Çakışma kontrolü buna göre yapılır.
    public int SlotMinutes { get; set; } = 30;

    // Çalışma saatleri bu saat diliminde değerlendirilir. Randevular
    // veritabanında UTC durur; kullanıcı ise yerel saatle düşünür.
    public string TimeZone { get; set; } = "Europe/Istanbul";

    // [WorkingHourStart, WorkingHourEnd) — 9..18 → 09:00 ile 17:30 arası
    // (30 dakikalık son randevu 18:00'de biter).
    public int WorkingHourStart { get; set; } = 9;
    public int WorkingHourEnd { get; set; } = 18;

    // System.DayOfWeek: 0 Pazar … 6 Cumartesi. Varsayılan hafta içi.
    public int[] WorkingDays { get; set; } = [1, 2, 3, 4, 5];

    // Randevu en erken bu kadar sonrasına alınabilir; "5 dakika sonrasına
    // randevu" pratikte işe yaramıyor.
    public int MinimumNoticeMinutes { get; set; } = 30;
}
