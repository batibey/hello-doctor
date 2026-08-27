using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Data;

// Applies migrations and seeds demo accounts on startup.
// Seeding is idempotent: it no-ops once any user exists, and never runs
// outside development.
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DbInitializer));

        await db.Database.MigrateAsync();

        // Demo hesapların şifresi "1234" ve hem README'de hem giriş ekranında
        // yazılı. Üretimde tohumlanırsa doktor yetkili hesaplar herkesin bildiği
        // şifreyle açılır — migration'lar uygulansın, veri uygulanmasın.
        if (!env.IsDevelopment())
        {
            logger.LogInformation(
                "Ortam {Environment}: demo verisi tohumlanmadı.", env.EnvironmentName);
            return;
        }

        if (await db.Users.AnyAsync())
            return;

        // İstemci ham parolayı değil türevini gönderdiği için tohumlama da aynı
        // türevi hash'lemeli. Anahtar alanları boş bırakılıyor: özel anahtarı
        // sarmalayacak parola yalnızca istemcide bilindiğinden, çift ilk girişte
        // istemcide üretilip yüklenir (POST /api/users/keys).
        const string demoPassword = "1234";
        string H() => hasher.Hash(AuthVerifier.Derive(demoPassword));

        var doctors = new[]
        {
            new User { Email = "dr.ayse@hellodoctor.com", PasswordHash = H(), FullName = "Ayşe Yılmaz",
                Role = UserRole.Doctor, Verification = DoctorVerification.Verified, MedicalLicenseNumber = "DEMO-1001", Specialty = "Kardiyoloji", Title = "Prof. Dr.", Rating = 4.9,
                ExperienceYears = 18, AvatarColor = "#EC4899",
                Bio = "Kalp ve damar hastalıkları uzmanı. 18 yıllık klinik deneyim." },
            new User { Email = "dr.mehmet@hellodoctor.com", PasswordHash = H(), FullName = "Mehmet Kaya",
                Role = UserRole.Doctor, Verification = DoctorVerification.Verified, MedicalLicenseNumber = "DEMO-1002", Specialty = "Dermatoloji", Title = "Uzm. Dr.", Rating = 4.7,
                ExperienceYears = 12, AvatarColor = "#0EA5E9",
                Bio = "Cilt sağlığı ve estetik dermatoloji uzmanı." },
            new User { Email = "dr.elif@hellodoctor.com", PasswordHash = H(), FullName = "Elif Demir",
                Role = UserRole.Doctor, Verification = DoctorVerification.Verified, MedicalLicenseNumber = "DEMO-1003", Specialty = "Çocuk Sağlığı", Title = "Dr.", Rating = 4.8,
                ExperienceYears = 9, AvatarColor = "#10B981",
                Bio = "Çocuk sağlığı ve hastalıkları uzmanı. Yenidoğan takibi." },
            new User { Email = "dr.canan@hellodoctor.com", PasswordHash = H(), FullName = "Canan Şahin",
                Role = UserRole.Doctor, Verification = DoctorVerification.Verified, MedicalLicenseNumber = "DEMO-1004", Specialty = "Psikiyatri", Title = "Uzm. Dr.", Rating = 4.9,
                ExperienceYears = 15, AvatarColor = "#8B5CF6",
                Bio = "Bireysel terapi ve psikiyatrik danışmanlık." },
        };

        var patients = new[]
        {
            // Geliştirmede doğrulama ekranını denemek için ilk hasta aynı zamanda
            // yönetici. Üretimde yönetici yetkisi yalnızca veritabanından verilir.
            new User { Email = "hasta@hellodoctor.com", PasswordHash = H(), FullName = "Ali Veli",
                Role = UserRole.Patient, Age = 34, BloodType = "A Rh+", AvatarColor = "#F59E0B",
                IsAdministrator = true },
            new User { Email = "zeynep@hellodoctor.com", PasswordHash = H(), FullName = "Zeynep Ak",
                Role = UserRole.Patient, Age = 28, BloodType = "0 Rh-", AvatarColor = "#EF4444" },
        };

        db.Users.AddRange(doctors);
        db.Users.AddRange(patients);

        var patient = patients[0];
        var doctor = doctors[0];

        db.Appointments.Add(new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            ScheduledAt = DateTime.UtcNow.AddHours(3),
            Type = AppointmentType.Video,
            Status = AppointmentStatus.Confirmed,
            Reason = "Genel kalp kontrolü",
        });

        var conv = AppDbContext.ConversationId(patient.Id, doctor.Id);
        db.Messages.AddRange(
            new ChatMessage { ConversationId = conv, SenderId = doctor.Id, RecipientId = patient.Id,
                Text = "Merhaba, randevunuz onaylandı. Görüşmede tansiyon değerlerinizi hazır bulundurun.",
                Read = true, SentAt = DateTime.UtcNow.AddMinutes(-40) },
            new ChatMessage { ConversationId = conv, SenderId = patient.Id, RecipientId = doctor.Id,
                Text = "Teşekkürler doktor, hazır olacağım.",
                Read = true, SentAt = DateTime.UtcNow.AddMinutes(-35) });

        await db.SaveChangesAsync();
    }
}
