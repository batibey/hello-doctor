using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;

namespace HelloDoctor.Api.Services;

// Sağlık verisine erişimin denetim kaydı.
//
// İstek logundan (RequestLoggingMiddleware) ayrı tutuluyor: o operasyonel,
// konsola akıyor ve kısa ömürlü. Bu ise "kim, kimin verisine, ne zaman erişti"
// sorusunun cevabı — saklanması ve sorgulanabilir olması gerekiyor.
//
// Kayıtlara erişilen içerik YAZILMAZ. Mesajlar zaten uçtan uca şifreli;
// denetim kaydına düz metin düşürmek bütün tasarımı boşa çıkarırdı.
public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, IHttpContextAccessor http, ILogger<AuditService> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;
    }

    public const string ConversationRead = "conversation.read";
    public const string ConversationList = "conversation.list";
    public const string AppointmentList = "appointment.list";
    public const string AppointmentStatusChanged = "appointment.status";
    public const string ProfileRead = "profile.read";
    public const string DataExported = "data.export";
    public const string AccountDeleted = "account.delete";
    public const string DoctorVerified = "doctor.verify";

    // Kendi verisine erişim kaydedilmez: her kullanıcı kendi ekranını açtığında
    // satır üretmek denetim kaydını kullanışsız hale getirirdi. Asıl soru
    // "başkasının verisine kim erişti".
    public void Record(string actorId, string subjectId, string action, string? resourceId = null)
    {
        if (actorId == subjectId) return;

        _db.AccessLogs.Add(new AccessLog
        {
            ActorId = actorId,
            SubjectId = subjectId,
            Action = action,
            ResourceId = resourceId,
            ClientIp = _http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
    }

    // Denetim kaydı yazılamadı diye asıl işlem geri alınmamalı; ama sessizce
    // de geçilmemeli, çünkü kaydın eksikliği mevzuat açısından önemli.
    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Denetim kaydı yazılamadı.");
        }
    }
}
