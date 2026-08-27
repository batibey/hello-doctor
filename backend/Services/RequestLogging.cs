using System.Diagnostics;
using System.Security.Claims;

namespace HelloDoctor.Api.Services;

// Her istek için tek satır yapılandırılmış log: yöntem, yol, durum, süre,
// kullanıcı ve iz kimliği.
//
// Sorgu dizesi BİLEREK loglanmıyor. SignalR JWT'yi ?access_token= ile taşıyor
// (WebSocket'te Authorization başlığı gönderilemediği için); o değer loglara
// düşerse log dosyasını gören herkes oturumu ele geçirebilir. Şifre sıfırlama
// bağlantısındaki token da aynı şekilde sorgu dizesinde geliyor.
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Sağlık kontrolleri saniyede bir çağrılıyor; logu boğmasınlar.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var status = context.Response.StatusCode;

            // 5xx hata, 4xx uyarı, gerisi bilgi. Böylece üretimde log seviyesini
            // yükseltince gürültü değil gerçek sorunlar kalır.
            var level = status >= 500 ? LogLevel.Error
                : status >= 400 ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(level,
                "{Method} {Path} → {StatusCode} ({ElapsedMs:0.0} ms) user={UserId} ip={ClientIp} trace={TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                status,
                elapsedMs,
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "-",
                context.Connection.RemoteIpAddress?.ToString() ?? "-",
                Activity.Current?.Id ?? context.TraceIdentifier);
        }
    }
}

public static class RequestLoggingExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();
}
