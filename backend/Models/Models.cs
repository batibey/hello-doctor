namespace HelloDoctor.Api.Models;

public enum UserRole { Patient, Doctor }

// Hekim yetkinliğinin doğrulanma durumu. Kayıt olurken kimse kendini
// doğrulanmış ilan edemez; Pending ile başlar ve yalnızca yönetici geçirir.
public enum DoctorVerification { NotApplicable, Pending, Verified, Rejected }

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; }
    public string AvatarColor { get; set; } = "#4F46E5";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Doctor-specific
    public string? Specialty { get; set; }
    public string? Title { get; set; }
    public double Rating { get; set; }
    public int ExperienceYears { get; set; }
    public string? Bio { get; set; }

    // Hekim doğrulaması. Doğrulanmamış hekim hasta listelerinde görünmez,
    // randevu alamaz ve mesajlaşamaz — 1219 sayılı Kanun uyarınca hekim
    // olmayan kişinin hasta ile tıbbi ilişki kurmaması gerekiyor.
    public DoctorVerification Verification { get; set; } = DoctorVerification.NotApplicable;
    public string? MedicalLicenseNumber { get; set; }   // diploma tescil numarası
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }             // onaylayan yöneticinin kullanıcı kimliği
    public string? VerificationNote { get; set; }       // ret gerekçesi ya da doğrulama kaynağı

    // Yönetici, doğrulama işlemlerini yapabilen hesap. Yalnızca veritabanından
    // atanır; kayıt akışıyla elde edilemez.
    public bool IsAdministrator { get; set; }

    // Patient-specific
    public int? Age { get; set; }
    public string? BloodType { get; set; }

    // E2EE anahtarları. Açık anahtar herkese verilir. Özel anahtar, kullanıcının
    // parolasından istemcide türetilen bir anahtarla sarmalanmış olarak durur —
    // sunucu sarmalamayı açacak malzemeye hiçbir zaman sahip olmaz.
    public string? PublicKey { get; set; }
    public string? WrappedPrivateKey { get; set; }
    public string? KeyWrapSalt { get; set; }
    public string? KeyWrapIv { get; set; }

    // Navigation
    public ICollection<Appointment> PatientAppointments { get; set; } = new List<Appointment>();
    public ICollection<Appointment> DoctorAppointments { get; set; } = new List<Appointment>();
}

// Sağlık verisine kimin ne zaman eriştiğinin kaydı. İstek logundan ayrı:
// o operasyonel ve kısa ömürlü, bu ise denetim kaydı ve saklanması gerekiyor.
// Erişilen içerik burada tutulmaz — yalnızca kimin hangi kayda eriştiği.
public class AccessLog
{
    public long Id { get; set; }
    public string ActorId { get; set; } = "";        // erişen kullanıcı
    public string SubjectId { get; set; } = "";      // verisine erişilen kişi
    public string Action { get; set; } = "";         // "conversation.read", "appointment.list" …
    public string? ResourceId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? ClientIp { get; set; }
}

// Aydınlatma metni ve açık rızanın kaydı. Metin sürümlenir: metin değişirse
// kullanıcının neyi kabul ettiği belirsiz kalmasın.
public class ConsentRecord
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string DocumentKey { get; set; } = "";    // "aydinlatma" | "acik-riza"
    public string DocumentVersion { get; set; } = "";
    public bool Granted { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? ClientIp { get; set; }

    public User? User { get; set; }
}

// Şifre sıfırlama bağlantısındaki token yalnızca e-postada ham haliyle bulunur;
// veritabanında hash'i durur ki veritabanı sızarsa hesaplar ele geçirilemesin.
public class PasswordResetToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}

public enum AppointmentStatus { Pending, Confirmed, Completed, Cancelled }
public enum AppointmentType { Message, Voice, Video }

public class Appointment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PatientId { get; set; } = "";
    public string DoctorId { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public AppointmentType Type { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? Patient { get; set; }
    public User? Doctor { get; set; }
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = ""; // "userA__userB", ordinal-sorted
    public string SenderId { get; set; } = "";
    public string RecipientId { get; set; } = "";

    // Encrypted = true ise Text base64 AES-GCM şifreli metindir ve sunucu onu
    // çözemez. Mesaj anahtarı her iki taraf için ayrı ayrı, ilgili tarafın açık
    // anahtarıyla şifrelenir; gönderen de kendi yazdığını okuyabilsin diye.
    public string Text { get; set; } = "";
    public bool Encrypted { get; set; }
    public string? Iv { get; set; }
    public string? KeyForSender { get; set; }
    public string? KeyForRecipient { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool Read { get; set; }

    // Navigation
    public User? Sender { get; set; }
    public User? Recipient { get; set; }
}

// ---- DTOs ----
// Password alanı ham parola değil, istemcide türetilmiş kimlik doğrulama
// değeridir (bkz. frontend/src/crypto/keys.js). Ham parola cihazdan çıkmaz.
public record LoginRequest(string Email, string Password, string Role);

// Kullanıcının kendi özel anahtar malzemesi; yalnızca giriş/kayıt yanıtında döner.
public record KeyBundle(string? PublicKey, string? WrappedPrivateKey,
    string? KeyWrapSalt, string? KeyWrapIv);

public record AuthResponse(string Token, UserDto User, KeyBundle Keys);

public record UserDto(string Id, string Email, string FullName, string Role, string AvatarColor,
    string? Specialty, string? Title, double Rating, int ExperienceYears, string? Bio, int? Age,
    string? BloodType, string? PublicKey, string Verification, bool IsAdministrator);

public record RegisterRequest(string Email, string Password, string FullName, string Role,
    int? Age, string? BloodType, string? Specialty, string? Title, string? Bio, int? ExperienceYears,
    string PublicKey, string WrappedPrivateKey, string KeyWrapSalt, string KeyWrapIv,
    string? MedicalLicenseNumber, bool AcceptedPrivacyNotice, bool AcceptedHealthDataConsent);

public record ForgotPasswordRequest(string Email);

// Sıfırlamada eski parola bilinmediği için eski özel anahtar açılamaz; istemci
// yeni bir anahtar çifti üretir ve eski mesajlar okunamaz hale gelir.
public record ResetPasswordRequest(string Token, string Password,
    string PublicKey, string WrappedPrivateKey, string KeyWrapSalt, string KeyWrapIv);

public record CreateAppointmentRequest(string DoctorId, string ScheduledAt, string Type, string? Reason);
public record UpdateAppointmentStatusRequest(string Status);
public record SendMessageRequest(string RecipientId, string Text, bool Encrypted,
    string? Iv, string? KeyForSender, string? KeyForRecipient);

public record VerifyDoctorRequest(string Decision, string? Note);   // "verified" | "rejected"
public record ConsentStatusDto(string DocumentKey, string Version, bool Granted, string At);
