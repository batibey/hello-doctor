using System.Security.Claims;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelloDoctor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    public UsersController(AppDbContext db) => _db = db;

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == CurrentUserId);
        if (u is null) return NotFound();
        return Ok(u.ToDto());
    }

    // İlk girişte istemci kendi anahtar çiftini üretip yükler. Sunucu ham
    // parolayı bilmediği için sarmalamayı kendisi yapamaz.
    //
    // Yalnızca bir kez yazılır: var olan anahtarın üzerine yazmak, o anahtarla
    // şifrelenmiş tüm geçmişi okunamaz hale getirirdi. Anahtar değişimi
    // yalnızca şifre sıfırlama akışından geçer.
    [HttpPost("keys")]
    public async Task<IActionResult> SetKeys([FromBody] KeyBundle req)
    {
        if (string.IsNullOrWhiteSpace(req.PublicKey) || string.IsNullOrWhiteSpace(req.WrappedPrivateKey)
            || string.IsNullOrWhiteSpace(req.KeyWrapSalt) || string.IsNullOrWhiteSpace(req.KeyWrapIv))
            return BadRequest(new { message = "Anahtar malzemesi eksik." });

        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == CurrentUserId);
        if (u is null) return NotFound();

        if (!string.IsNullOrEmpty(u.PublicKey))
            return Conflict(new { message = "Bu hesapta zaten bir anahtar var." });

        u.PublicKey = req.PublicKey;
        u.WrappedPrivateKey = req.WrappedPrivateKey;
        u.KeyWrapSalt = req.KeyWrapSalt;
        u.KeyWrapIv = req.KeyWrapIv;
        await _db.SaveChangesAsync();

        return Ok(u.ToKeyBundle());
    }

    [HttpGet("doctors")]
    public async Task<IActionResult> Doctors([FromQuery] string? q)
    {
        var query = _db.Users.AsNoTracking().Where(u => u.Role == UserRole.Doctor);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName, term) ||
                (u.Specialty != null && EF.Functions.ILike(u.Specialty, term)));
        }

        var doctors = await query.OrderByDescending(u => u.Rating).ToListAsync();
        return Ok(doctors.Select(d => d.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (u is null) return NotFound();
        return Ok(u.ToDto());
    }
}
