using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HelloDoctor.Api.Services;

public static class HealthEndpoints
{
    // Konteyner orkestratörleri iki ayrı soru sorar:
    //
    //   /health/live  — süreç ayakta mı? Bağımlılıklara bakmaz. Bu başarısız
    //                   olursa konteyner yeniden başlatılır. Veritabanı geçici
    //                   olarak düştüğünde uygulamayı yeniden başlatmak işe
    //                   yaramaz, o yüzden buraya DB kontrolü konmaz.
    //
    //   /health/ready — istek karşılayabilir mi? Veritabanına bakar. Başarısızsa
    //                   yük dengeleyici trafiği bu örneğe göndermeyi keser ama
    //                   süreci öldürmez.
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false, // hiçbir kontrol çalışmaz, yalnızca süreç
            ResponseWriter = Write,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("ready"),
            ResponseWriter = Write,
        });
    }

    // Uç noktalar kimlik doğrulaması istemiyor (orkestratör token taşıyamaz),
    // bu yüzden gövde ayrıntı sızdırmamalı: yalnızca kontrol adı ve durumu.
    // İstisna metni ve süre bilgisi dışarı verilmez.
    private static Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
