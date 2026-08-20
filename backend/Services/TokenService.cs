using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HelloDoctor.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace HelloDoctor.Api.Services;

public class TokenService
{
    public const string SecretKey = "hello-doctor-super-secret-demo-key-change-in-prod-0123456789";

    public string CreateToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("email", user.Email),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "HelloDoctor",
            audience: "HelloDoctor",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
