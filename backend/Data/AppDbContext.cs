using HelloDoctor.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    // Deterministic conversation key for a pair of users.
    public static string ConversationId(string a, string b)
    {
        var arr = new[] { a, b };
        Array.Sort(arr, StringComparer.Ordinal);
        return $"{arr[0]}__{arr[1]}";
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(36);
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(400).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.AvatarColor).HasMaxLength(9);
            e.Property(x => x.Specialty).HasMaxLength(100);
            e.Property(x => x.Title).HasMaxLength(50);
            e.Property(x => x.BloodType).HasMaxLength(10);
            e.Property(x => x.Bio).HasMaxLength(1000);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            // One account per e-mail, case-insensitive lookups hit this index.
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Role);
        });

        b.Entity<Appointment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(36);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.Patient)
                .WithMany(u => u.PatientAppointments)
                .HasForeignKey(x => x.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Doctor)
                .WithMany(u => u.DoctorAppointments)
                .HasForeignKey(x => x.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            // "my appointments, soonest first" — the only list query we run.
            e.HasIndex(x => new { x.PatientId, x.ScheduledAt });
            e.HasIndex(x => new { x.DoctorId, x.ScheduledAt });
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(36);
            e.Property(x => x.ConversationId).HasMaxLength(80).IsRequired();
            e.Property(x => x.Text).HasMaxLength(4000).IsRequired();

            e.HasOne(x => x.Sender)
                .WithMany()
                .HasForeignKey(x => x.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Recipient)
                .WithMany()
                .HasForeignKey(x => x.RecipientId)
                .OnDelete(DeleteBehavior.Restrict);

            // Opening a thread: WHERE ConversationId = x ORDER BY SentAt
            e.HasIndex(x => new { x.ConversationId, x.SentAt });
            // Unread badge count: WHERE RecipientId = me AND Read = false
            e.HasIndex(x => new { x.RecipientId, x.Read });
        });
    }
}
