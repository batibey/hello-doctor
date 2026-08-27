namespace HelloDoctor.Api.Services;

// Politika adı hem kayıt hem de [EnableRateLimiting] tarafında geçtiği için
// tek yerde tutulur; yazım hatası sınırlamayı sessizce devre dışı bırakabilir.
public static class RateLimitPolicies
{
    public const string Login = "login";
}
