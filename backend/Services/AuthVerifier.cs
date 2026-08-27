using System.Security.Cryptography;
using System.Text;

namespace HelloDoctor.Api.Services;

// Ham parola cihazdan hiç çıkmaz. İstemci sunucuya parolanın bu türevini
// gönderir; sunucu da onu PBKDF2 ile hash'leyip saklar (PasswordService).
//
// Bunun sebebi E2EE: özel anahtar, parolanın *başka* bir türevinden üretilen
// anahtarla sarmalanıyor. Sunucu ham parolayı görseydi aynı sarmalama
// anahtarını türetip özel anahtarı açabilir, "sunucu okuyamaz" iddiası
// anlamını yitirirdi.
//
// Karşılığı: frontend/src/crypto/keys.js → authVerifier()
// İki taraf birebir aynı dizeyi üretmeli, yoksa giriş çalışmaz.
public static class AuthVerifier
{
    private const string Prefix = "hellodoctor:auth:v1:";

    public static string Derive(string password) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Prefix + password)));
}
