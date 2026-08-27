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

    public AuthController(AppDbContext db, TokenService tokens, PasswordService passwords)
    {
        _db = db;
        _tokens = tokens;
        _passwords = passwords;
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
        return Ok(new AuthResponse(token, user.ToDto()));
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
}
