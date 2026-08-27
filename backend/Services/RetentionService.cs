using HelloDoctor.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelloDoctor.Api.Services;

// Saklama süresi dolan kayıtları buda. Günde bir kez çalışır.
//
// Varsayılan olarak yalnızca teknik kayıtlara dokunur (denetim kaydı, kullanılmış
// sıfırlama token'ları). Mesaj ve randevu budaması yapılandırmayla açılır ve
// varsayılanı kapalıdır: tıbbi kaydı erken silmek de mevzuata aykırı olabilir,
// bu yüzden süre Bakanlık görüşü netleşmeden kendiliğinden işlememeli.
public class RetentionService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ComplianceOptions _options;
    private readonly ILogger<RetentionService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public RetentionService(IServiceProvider services, IOptions<ComplianceOptions> options,
        ILogger<RetentionService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Açılışta hemen çalıştırmıyoruz: migration ve tohumlama bitsin.
        await Task.Delay(TimeSpan.FromMinutes(1), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Budama başarısız olursa uygulama çalışmaya devam etmeli.
                _logger.LogError(ex, "Saklama süresi budaması başarısız.");
            }

            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var total = 0;

        if (_options.AccessLogRetentionDays > 0)
        {
            var cutoff = now.AddDays(-_options.AccessLogRetentionDays);
            var n = await db.AccessLogs.Where(x => x.At < cutoff).ExecuteDeleteAsync(ct);
            if (n > 0) { _logger.LogInformation("{Count} denetim kaydı budandı.", n); total += n; }
        }

        if (_options.UsedResetTokenRetentionDays > 0)
        {
            var cutoff = now.AddDays(-_options.UsedResetTokenRetentionDays);
            var n = await db.PasswordResetTokens
                .Where(t => t.CreatedAt < cutoff && (t.UsedAt != null || t.ExpiresAt < now))
                .ExecuteDeleteAsync(ct);
            if (n > 0) { _logger.LogInformation("{Count} kullanılmış sıfırlama kaydı budandı.", n); total += n; }
        }

        if (_options.MessageRetentionDays > 0)
        {
            var cutoff = now.AddDays(-_options.MessageRetentionDays);
            var n = await db.Messages.Where(m => m.SentAt < cutoff).ExecuteDeleteAsync(ct);
            if (n > 0) { _logger.LogInformation("{Count} mesaj budandı.", n); total += n; }
        }

        if (_options.AppointmentRetentionDays > 0)
        {
            var cutoff = now.AddDays(-_options.AppointmentRetentionDays);
            var n = await db.Appointments.Where(a => a.ScheduledAt < cutoff).ExecuteDeleteAsync(ct);
            if (n > 0) { _logger.LogInformation("{Count} randevu budandı.", n); total += n; }
        }

        if (total == 0) _logger.LogDebug("Saklama süresi budaması: silinecek kayıt yok.");
    }
}
