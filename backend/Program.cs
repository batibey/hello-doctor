using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Hubs;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Üretimde JSON: log toplayıcılar (Loki, CloudWatch, Datadog) satırları
// ayrıştırabilsin ve alanlara göre sorgulanabilsin. Geliştirmede okunabilir
// konsol formatı kalır.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.TimestampFormat = "o";
        o.UseUtcTimestamp = true;
    });
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres tanımlı değil.");

// Factory for SignalR hubs, which cannot take a scoped dependency…
builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseNpgsql(connectionString));
// …plus a scoped context for controllers. The options must stay singleton so the
// factory above can consume them.
builder.Services.AddDbContext<AppDbContext>(
    opt => opt.UseNpgsql(connectionString),
    optionsLifetime: ServiceLifetime.Singleton);

// JWT signing key comes from configuration (Jwt__Key env var outside development).
// Fail fast at startup rather than issuing tokens signed with a missing/weak key.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
jwtOptions.Validate();

builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PasswordService>();

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<EmailSender>();

builder.Services.Configure<AppointmentOptions>(
    builder.Configuration.GetSection(AppointmentOptions.SectionName));

builder.Services.Configure<IceOptions>(builder.Configuration.GetSection(IceOptions.SectionName));
builder.Services.AddHttpClient();
// Önbellek örnek içinde tutulduğu için singleton.
builder.Services.AddSingleton<IceServerProvider>();

// Her origin'e açık bir politika AllowCredentials ile birleşince, herhangi bir
// sitenin kullanıcının tarayıcısı üzerinden kimlikli istek atmasına izin verir.
// Üretimde origin listesi açıkça verilmeli; verilmezse uygulama açılmaz.
const string CorsPolicy = "frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

if (!builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
    throw new InvalidOperationException(
        "Cors:AllowedOrigins tanımlı değil. Üretimde izin verilen origin'leri belirtin " +
        "(örn. Cors__AllowedOrigins__0=https://hellodoctor.example).");

builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
{
    if (allowedOrigins.Length > 0)
        p.WithOrigins(allowedOrigins);
    else
        // Yalnızca geliştirmeye düşer: LAN IP'si ve tünel adresi sürekli değişiyor.
        p.SetIsOriginAllowed(_ => true);

    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

// Giriş uç noktasına kaba kuvvet sınırı: IP başına dakikada N deneme.
// Geliştirmede yüksek tutulur, yoksa uçtan uca test betiği (her koşuda birkaç
// giriş yapıyor) arka arkaya çalıştırıldığında kendi kendini kilitler.
var loginPerMinute = builder.Configuration.GetValue<int?>("RateLimit:LoginPerMinute") ?? 5;

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(RateLimitPolicies.Login, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPerMinute,
                Window = TimeSpan.FromMinutes(1),
            }));

    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();

        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Çok fazla giriş denemesi. Biraz bekleyip tekrar deneyin." }, token);
    };
});

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        // Allow SignalR to pass the JWT via query string (?access_token=)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Veritabanı erişilebilirliği "ready" etiketiyle: canlılık kontrolü ona bakmaz.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"]);

// ---- HTTPS / HSTS ----
// Geliştirmede kapalı: Vite proxy'si http://localhost:5088'e gidiyor ve yerel
// TLS yok; yönlendirme açık olsa geliştirme tamamen kırılırdı.
var httpsSection = builder.Configuration.GetSection(HttpsOptions.SectionName);
var httpsOptions = httpsSection.Get<HttpsOptions>() ?? new HttpsOptions();
var httpsEnabled = !builder.Environment.IsDevelopment() && httpsOptions.RedirectToHttps;

builder.Services.Configure<HttpsOptions>(httpsSection);

if (httpsEnabled)
{
    builder.Services.AddHttpsRedirection(o => o.HttpsPort = httpsOptions.HttpsPort);

    builder.Services.AddHsts(o =>
    {
        o.MaxAge = TimeSpan.FromDays(httpsOptions.HstsMaxAgeDays);
        o.IncludeSubDomains = httpsOptions.HstsIncludeSubdomains;
        o.Preload = httpsOptions.HstsPreload;
    });
}

if (httpsOptions.UseForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Varsayılan liste loopback içerir. Açıkça vekil verildiyse yalnızca
        // ona güvenilir: rastgele bir kaynağın X-Forwarded-For uydurup giriş
        // hız sınırını IP taklidiyle aşmasını engeller.
        if (httpsOptions.KnownProxies.Length > 0 || httpsOptions.KnownNetworks.Length > 0)
        {
            o.KnownProxies.Clear();
            o.KnownNetworks.Clear();
        }

        foreach (var ip in httpsOptions.KnownProxies)
            if (IPAddress.TryParse(ip, out var parsed)) o.KnownProxies.Add(parsed);

        foreach (var cidr in httpsOptions.KnownNetworks)
        {
            var parts = cidr.Split('/');
            // System.Net.IPNetwork ile karışmasın: KnownNetworks ASP.NET'inkini bekliyor.
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], out var length))
                o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
        }
    });
}

var app = builder.Build();

// En başta olmalı: sonraki her katman isteğin şemasına ve gerçek istemci
// IP'sine bakıyor (HTTPS yönlendirmesi, HSTS, giriş hız sınırı).
if (httpsOptions.UseForwardedHeaders)
    app.UseForwardedHeaders();

// Yetkilendirmeden ÖNCE: UseAuthorization yetkisiz isteği 401 ile kısa devre
// yapıyor, sonrasına konsaydı başarısız kimlik denemeleri hiç loglanmazdı.
// Kullanıcı kimliği yine de yazılıyor, çünkü satır boru hattı geri sarılırken
// (_next tamamlandıktan sonra) üretiliyor ve o noktada User dolu.
app.UseRequestLogging();

if (httpsEnabled)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<CallHub>("/hubs/call");
app.MapHealthEndpoints();
app.MapGet("/", () => "HelloDoctor API çalışıyor 🩺");

// Apply migrations and seed demo data on startup.
await DbInitializer.InitializeAsync(app.Services);

app.Run();
