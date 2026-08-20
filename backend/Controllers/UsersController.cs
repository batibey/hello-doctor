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
