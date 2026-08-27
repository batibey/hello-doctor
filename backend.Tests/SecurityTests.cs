using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HelloDoctor.Api.Data;
using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace HelloDoctor.Api.Tests;

public class PasswordServiceTests
{
    private readonly PasswordService _sut = new();

    [Fact]
    public void Dogru_parola_dogrulanir()
    {
        var hash = _sut.Hash("SifreTest123");
        Assert.True(_sut.Verify("SifreTest123", hash));
    }

    [Fact]
    public void Yanlis_parola_reddedilir()
    {
        var hash = _sut.Hash("SifreTest123");
        Assert.False(_sut.Verify("SifreTest124", hash));
        Assert.False(_sut.Verify("", hash));
    }

    [Fact]
    public void Ayni_parola_her_seferinde_farkli_hash_uretir()
    {
        // Tuz rastgele olmalı: aynı parolayı kullanan iki hesap veritabanında
        // aynı satırı taşımamalı, yoksa biri kırılınca hepsi kırılır.
        Assert.NotEqual(_sut.Hash("ayni"), _sut.Hash("ayni"));
    }

    [Fact]
    public void Hash_bicimi_tur_tuz_anahtar()
    {
        var parts = _sut.Hash("x").Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal(100_000, int.Parse(parts[0]));
        Assert.Equal(16, Convert.FromBase64String(parts[1]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[2]).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bozuk")]
    [InlineData("a.b")]
    [InlineData("abc.tuz.anahtar")]      // tur sayı değil
    [InlineData("100000.++.++")]          // base64 değil
    public void Bozuk_hash_cokmez_false_doner(string hash) =>
        Assert.False(_sut.Verify("herhangi", hash));

    [Fact]
    public void Parola_hash_icinde_duz_metin_gecmez()
    {
        var hash = _sut.Hash("CokGizliParola");
        Assert.DoesNotContain("CokGizliParola", hash);
    }
}

public class AuthVerifierTests
{
    // Bu sözleşme iki dilde ayrı ayrı uygulanıyor: frontend/src/crypto/keys.js
    // içindeki authVerifier() ile birebir aynı diziyi üretmek zorunda. Biri
    // değişirse giriş sessizce kırılır — sabit değerle kilitliyoruz.
    [Fact]
    public void Frontend_ile_ayni_degeri_uretir()
    {
        var expected = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes("hellodoctor:auth:v1:1234")));

        Assert.Equal(expected, AuthVerifier.Derive("1234"));
        // Sabit değer: taraflardan biri öneki ya da algoritmayı değiştirirse yakalanır.
        Assert.Equal("lMeZLVx30ZkK6k1XucaAyaO52JbqkZn2yeDPf/ebh3g=", AuthVerifier.Derive("1234"));
    }

    [Fact]
    public void Ham_parola_turevden_okunamaz()
    {
        var verifier = AuthVerifier.Derive("CokGizliParola");
        Assert.DoesNotContain("CokGizliParola", verifier);
    }

    [Fact]
    public void Farkli_parolalar_farkli_turev_uretir() =>
        Assert.NotEqual(AuthVerifier.Derive("a"), AuthVerifier.Derive("b"));

    [Fact]
    public void Turev_sabit_uzunlukta()
    {
        // Sunucu tarafındaki uzunluk kontrolü (>= 32) buna dayanıyor.
        Assert.Equal(44, AuthVerifier.Derive("kısa").Length);
        Assert.Equal(44, AuthVerifier.Derive(new string('u', 500)).Length);
    }
}

public class JwtOptionsTests
{
    [Fact]
    public void Gecerli_anahtar_kabul_edilir() =>
        new JwtOptions { Key = new string('k', 32) }.Validate();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kisa")]
    public void Zayif_anahtar_reddedilir(string key)
    {
        // Zayıf anahtarla token imzalamaktansa açılmayı reddetmek tercih edilir.
        var ex = Assert.Throws<InvalidOperationException>(
            () => new JwtOptions { Key = key }.Validate());
        Assert.Contains("JWT", ex.Message);
    }

    // HMAC-SHA256 en az 256 bit ister; sınırın tam iki yanı.
    [Fact]
    public void Sinirin_bir_bayt_altisi_reddedilir()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new JwtOptions { Key = new string('k', JwtOptions.MinimumKeyBytes - 1) }.Validate());
        Assert.Contains("31 bayt", ex.Message);
    }

    [Fact]
    public void Sinir_degeri_kabul_edilir() =>
        new JwtOptions { Key = new string('k', JwtOptions.MinimumKeyBytes) }.Validate();

    // Uzunluk bayt cinsinden ölçülmeli: 20 Türkçe karakter 32 karakter sayılır
    // ama UTF-8'de daha fazla bayt eder; karakter sayarsak zayıf anahtar geçerdi.
    [Fact]
    public void Uzunluk_karakter_degil_bayt_olarak_olculur()
    {
        // 16 karakter, UTF-8'de 32 bayt (her biri 2 bayt).
        new JwtOptions { Key = new string('ş', 16) }.Validate();
        Assert.Throws<InvalidOperationException>(
            () => new JwtOptions { Key = new string('ş', 15) }.Validate());
    }
}

public class TokenServiceTests
{
    private static TokenService Create() =>
        new(Options.Create(new JwtOptions
        {
            Key = new string('k', 48),
            Issuer = "HelloDoctor",
            Audience = "HelloDoctor",
            ExpiryDays = 7,
        }));

    [Fact]
    public void Token_kimlik_ve_rol_tasiyor()
    {
        var user = new User { Email = "a@b.com", FullName = "Ali Veli", Role = UserRole.Doctor };
        var raw = Create().CreateToken(user);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(raw);

        Assert.Equal(user.Id, jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("Doctor", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("HelloDoctor", jwt.Issuer);
    }

    [Fact]
    public void Token_parola_malzemesi_tasimiyor()
    {
        var user = new User { Email = "a@b.com", PasswordHash = "GIZLI-HASH", Role = UserRole.Patient,
            WrappedPrivateKey = "GIZLI-ANAHTAR" };
        var raw = Create().CreateToken(user);

        // Token base64; içeriği herkes okuyabilir. Parola hash'i ya da sarmalı
        // özel anahtar oraya düşerse E2EE'nin anlamı kalmaz.
        Assert.DoesNotContain("GIZLI-HASH", raw);
        Assert.DoesNotContain("GIZLI-ANAHTAR", raw);
    }
}

public class ConversationIdTests
{
    [Fact]
    public void Siralamadan_bagimsiz_ayni_sonuc() =>
        Assert.Equal(AppDbContext.ConversationId("b", "a"), AppDbContext.ConversationId("a", "b"));

    [Fact]
    public void Farkli_ciftler_farkli_kimlik() =>
        Assert.NotEqual(AppDbContext.ConversationId("a", "b"), AppDbContext.ConversationId("a", "c"));

    [Fact]
    public void Ordinal_siralama_kullanilir()
    {
        // Kültüre duyarlı sıralama makineden makineye değişebilir; aynı çift
        // için farklı kimlik üretmek sohbeti ikiye böler.
        Assert.Equal("A__a", AppDbContext.ConversationId("a", "A"));
    }

    [Fact]
    public void Ayirici_iki_alt_cizgi()
    {
        var id = AppDbContext.ConversationId("x", "y");
        Assert.Equal("x__y", id);
    }

    [Fact]
    public void Uretilen_kimlik_sutuna_sigar()
    {
        // İki GUID (36) + ayırıcı (2) = 74; sütun sınırı 80.
        var id = AppDbContext.ConversationId(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        Assert.True(id.Length <= 80, $"{id.Length} karakter, sütun 80");
    }
}
