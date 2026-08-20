using System.Text;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Hubs;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

const string CorsPolicy = "frontend";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
    .SetIsOriginAllowed(_ => true) // allow any origin for the demo (Vite dev server / LAN)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

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

var app = builder.Build();

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<CallHub>("/hubs/call");
app.MapGet("/", () => "HelloDoctor API çalışıyor 🩺");

// Apply migrations and seed demo data on startup.
await DbInitializer.InitializeAsync(app.Services);

app.Run();
