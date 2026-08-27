using System.Security.Cryptography;
using System.Text;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly PasswordService _passwords;
    private readonly EmailSender _email;
    private readonly ILogger<AuthController> _logger;

    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    public AuthController(AppDbContext db, TokenService tokens, PasswordService passwords,
        EmailSender email, ILogger<AuthController> logger)
    {
        _db = db;
        _tokens = tokens;
        _passwords = passwords;
        _email = email;
        _logger = logger;
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (!Enum.TryParse<UserRole>(req.Role, ignoreCase: true, out var role))
            return BadRequest(new { message = "Geçersiz rol." });

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.Role == role);

        if (user is null || !_passwords.Verify(req.Password ?? "", user.PasswordHash))
            return Unauthorized(new { message = "E-posta, şifre veya rol hatalı." });

        var token = _tokens.CreateToken(user);
        return Ok(new AuthResponse(token, user.ToDto(), user.ToKeyBundle()));
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (!Enum.TryParse<UserRole>(req.Role, ignoreCase: true, out var role))
            return BadRequest(new { message = "Geçersiz rol." });

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (!IsPlausibleEmail(email))
            return BadRequest(new { message = "Geçerli bir e-posta adresi girin." });

        var fullName = (req.FullName ?? "").Trim();
        if (fullName.Length < 2)
            return BadRequest(new { message = "Ad soyad en az 2 karakter olmalı." });

        // İstemci ham parolayı değil, ondan türetilmiş sabit uzunlukta bir değer
        // gönderiyor; uzunluk kuralı orada uygulanıyor. Burada yalnızca boş ya da
        // biçimsiz gelmediğini doğruluyoruz.
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 32)
            return BadRequest(new { message = "Şifre doğrulayıcısı geçersiz." });

        if (string.IsNullOrWhiteSpace(req.PublicKey) || string.IsNullOrWhiteSpace(req.WrappedPrivateKey)
            || string.IsNullOrWhiteSpace(req.KeyWrapSalt) || string.IsNullOrWhiteSpace(req.KeyWrapIv))
            return BadRequest(new { message = "Şifreleme anahtarları eksik." });

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email))
            return Conflict(new { message = "Bu e-posta adresi zaten kayıtlı." });

        var user = new User
        {
            Email = email,
            PasswordHash = _passwords.Hash(req.Password),
            FullName = fullName,
            Role = role,
            AvatarColor = PickAvatarColor(email),
            PublicKey = req.PublicKey,
            WrappedPrivateKey = req.WrappedPrivateKey,
            KeyWrapSalt = req.KeyWrapSalt,
            KeyWrapIv = req.KeyWrapIv,
        };

        if (role == UserRole.Patient)
        {
            user.Age = req.Age;
            user.BloodType = req.BloodType?.Trim();
        }
        else
        {
            user.Specialty = req.Specialty?.Trim();
            user.Title = req.Title?.Trim();
            user.Bio = req.Bio?.Trim();
            user.ExperienceYears = req.ExperienceYears ?? 0;
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _tokens.CreateToken(user);
        return Ok(new AuthResponse(token, user.ToDto(), user.ToKeyBundle()));
    }

    // Yanıt her durumda aynı: var olmayan adres için farklı cevap vermek,
    // kimlerin kayıtlı olduğunu sızdırırdı.
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var ok = new { message = "Adres kayıtlıysa sıfırlama bağlantısı gönderildi." };

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (user is null) return Ok(ok);

        // Bekleyen eski istekleri geçersiz kıl: aynı anda birden fazla geçerli
        // bağlantı dolaşmasın.
        var pending = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var t in pending) t.UsedAt = DateTime.UtcNow;

        var rawToken = GenerateToken();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.Add(ResetTokenLifetime),
        });
        await _db.SaveChangesAsync();

        try
        {
            await _email.SendPasswordResetAsync(user.Email, user.FullName, rawToken);
        }
        catch (Exception ex)
        {
            // Gönderim hatası kullanıcıya yansıtılmıyor; aksi halde bu uç nokta
            // hangi adreslerin kayıtlı olduğunu ele verirdi.
            _logger.LogError(ex, "Şifre sıfırlama e-postası gönderilemedi: {Email}", user.Email);
        }

        return Ok(ok);
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            return BadRequest(new { message = "Bağlantı geçersiz." });

        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 32)
            return BadRequest(new { message = "Şifre doğrulayıcısı geçersiz." });

        if (string.IsNullOrWhiteSpace(req.PublicKey) || string.IsNullOrWhiteSpace(req.WrappedPrivateKey)
            || string.IsNullOrWhiteSpace(req.KeyWrapSalt) || string.IsNullOrWhiteSpace(req.KeyWrapIv))
            return BadRequest(new { message = "Şifreleme anahtarları eksik." });

        var hash = HashToken(req.Token);
        var entry = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (entry is null || entry.UsedAt != null || entry.ExpiresAt <= DateTime.UtcNow || entry.User is null)
            return BadRequest(new { message = "Bağlantı geçersiz veya süresi dolmuş." });

        entry.UsedAt = DateTime.UtcNow;
        entry.User.PasswordHash = _passwords.Hash(req.Password);

        // Eski özel anahtar eski parolayla sarmalıydı ve artık açılamaz; istemci
        // yeni bir çift üretti. Sıfırlama öncesi mesajlar okunamaz hale gelir.
        entry.User.PublicKey = req.PublicKey;
        entry.User.WrappedPrivateKey = req.WrappedPrivateKey;
        entry.User.KeyWrapSalt = req.KeyWrapSalt;
        entry.User.KeyWrapIv = req.KeyWrapIv;

        await _db.SaveChangesAsync();

        var token = _tokens.CreateToken(entry.User);
        return Ok(new AuthResponse(token, entry.User.ToDto(), entry.User.ToKeyBundle()));
    }

    // Convenience for the demo: list the seeded test accounts (password is always "1234").
    // Yalnızca geliştirmede açık. Üretimde bu uç nokta kimlik doğrulaması olmadan
    // tüm kullanıcıların e-postasını dökerdi.
    [HttpGet("demo-accounts")]
    public async Task<IActionResult> DemoAccounts([FromServices] IHostEnvironment env)
    {
        if (!env.IsDevelopment()) return NotFound();

        var accounts = await _db.Users
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .Select(u => new { u.Email, Password = "1234", Role = u.Role.ToString(), u.FullName, u.Specialty })
            .ToListAsync();
        return Ok(accounts);
    }

    private static bool IsPlausibleEmail(string email) =>
        email.Length is >= 5 and <= 200
        && email.Count(c => c == '@') == 1
        && email.IndexOf('@') > 0
        && email.LastIndexOf('.') > email.IndexOf('@') + 1
        && !email.EndsWith('.');

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string HashToken(string raw) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string PickAvatarColor(string seed)
    {
        string[] palette = ["#4F46E5", "#EC4899", "#0EA5E9", "#10B981", "#F59E0B", "#8B5CF6", "#EF4444"];
        return palette[Math.Abs(seed.GetHashCode(StringComparison.Ordinal)) % palette.Length];
    }
}
