namespace HelloDoctor.Api.Models;

public enum UserRole { Patient, Doctor }

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

    // Patient-specific
    public int? Age { get; set; }
    public string? BloodType { get; set; }

    // Navigation
    public ICollection<Appointment> PatientAppointments { get; set; } = new List<Appointment>();
    public ICollection<Appointment> DoctorAppointments { get; set; } = new List<Appointment>();
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
    public string Text { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool Read { get; set; }

    // Navigation
    public User? Sender { get; set; }
    public User? Recipient { get; set; }
}

// ---- DTOs ----
public record LoginRequest(string Email, string Password, string Role);
public record AuthResponse(string Token, UserDto User);
public record UserDto(string Id, string Email, string FullName, string Role, string AvatarColor,
    string? Specialty, string? Title, double Rating, int ExperienceYears, string? Bio, int? Age, string? BloodType);
public record CreateAppointmentRequest(string DoctorId, string ScheduledAt, string Type, string? Reason);
public record UpdateAppointmentStatusRequest(string Status);
public record SendMessageRequest(string RecipientId, string Text);
