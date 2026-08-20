using System.Text;

namespace HelloDoctor.Api.Services;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC-SHA256 signing key. Supply via the Jwt__Key environment variable
    /// outside of development — never commit a production value.
    /// </summary>
    public string Key { get; set; } = "";

    public string Issuer { get; set; } = "HelloDoctor";
    public string Audience { get; set; } = "HelloDoctor";
    public int ExpiryDays { get; set; } = 7;

    /// <summary>HMAC-SHA256 requires a key of at least 256 bits.</summary>
    public const int MinimumKeyBytes = 32;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new InvalidOperationException(
                "JWT imza anahtarı tanımlı değil. Jwt__Key ortam değişkenini ayarlayın " +
                "(örn. export Jwt__Key=\"$(openssl rand -base64 48)\").");

        var bytes = Encoding.UTF8.GetByteCount(Key);
        if (bytes < MinimumKeyBytes)
            throw new InvalidOperationException(
                $"JWT imza anahtarı çok kısa: {bytes} bayt. HMAC-SHA256 en az {MinimumKeyBytes} bayt gerektirir.");
    }
}
