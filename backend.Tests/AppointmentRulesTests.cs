using HelloDoctor.Api.Models;
using HelloDoctor.Api.Services;
using Xunit;

namespace HelloDoctor.Api.Tests;

public class AppointmentTransitionTests
{
    // Onay doktorda
    [Theory]
    [InlineData(UserRole.Doctor, true)]
    [InlineData(UserRole.Patient, false)]
    public void Onaylama_yalnizca_doktorda(UserRole role, bool allowed) =>
        Assert.Equal(allowed, AppointmentRules.CanTransition(
            role, AppointmentStatus.Pending, AppointmentStatus.Confirmed));

    // Tamamlandı işaretlemek klinik kayıt: doktorda
    [Theory]
    [InlineData(UserRole.Doctor, true)]
    [InlineData(UserRole.Patient, false)]
    public void Tamamlandi_yalnizca_doktorda(UserRole role, bool allowed) =>
        Assert.Equal(allowed, AppointmentRules.CanTransition(
            role, AppointmentStatus.Confirmed, AppointmentStatus.Completed));

    // İptal her iki tarafta
    [Theory]
    [InlineData(UserRole.Doctor, AppointmentStatus.Pending)]
    [InlineData(UserRole.Doctor, AppointmentStatus.Confirmed)]
    [InlineData(UserRole.Patient, AppointmentStatus.Pending)]
    [InlineData(UserRole.Patient, AppointmentStatus.Confirmed)]
    public void Iptal_her_iki_tarafta(UserRole role, AppointmentStatus from) =>
        Assert.True(AppointmentRules.CanTransition(role, from, AppointmentStatus.Cancelled));

    // Sonlanmış randevu hiçbir yöne gitmez
    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void Sonlanmis_randevu_degismez(AppointmentStatus terminal)
    {
        foreach (var role in new[] { UserRole.Doctor, UserRole.Patient })
        foreach (var to in Enum.GetValues<AppointmentStatus>())
            Assert.False(AppointmentRules.CanTransition(role, terminal, to),
                $"{role}: {terminal} → {to} engellenmeliydi");
    }

    [Fact]
    public void Onaylanmadan_tamamlanamaz() =>
        Assert.False(AppointmentRules.CanTransition(
            UserRole.Doctor, AppointmentStatus.Pending, AppointmentStatus.Completed));

    [Fact]
    public void Ayni_duruma_gecis_yapilamaz() =>
        Assert.False(AppointmentRules.CanTransition(
            UserRole.Doctor, AppointmentStatus.Confirmed, AppointmentStatus.Confirmed));

    // Hata mesajı reddedilen her geçiş için anlamlı olmalı; kullanıcı neden
    // olmadığını görmeli.
    [Fact]
    public void Reddedilen_gecisler_kullaniciya_sebep_soyluyor()
    {
        foreach (var role in new[] { UserRole.Doctor, UserRole.Patient })
        foreach (var from in Enum.GetValues<AppointmentStatus>())
        foreach (var to in Enum.GetValues<AppointmentStatus>())
        {
            if (AppointmentRules.CanTransition(role, from, to)) continue;
            var msg = AppointmentRules.TransitionError(role, from, to);
            Assert.False(string.IsNullOrWhiteSpace(msg));
            Assert.EndsWith(".", msg);
        }
    }
}

public class AppointmentValidationTests
{
    // Istanbul yıl boyu UTC+3, yaz saati uygulaması yok.
    private static readonly TimeZoneInfo Istanbul =
        TimeZoneInfo.CreateCustomTimeZone("test-ist", TimeSpan.FromHours(3), "Istanbul", "Istanbul");

    private static AppointmentRules Rules(AppointmentOptions? o = null) =>
        new(o ?? new AppointmentOptions(), Istanbul);

    // 2026-08-27 Perşembe. Yerel saat = UTC + 3.
    private static DateTime Local(int day, int hour, int minute = 0) =>
        new DateTime(2026, 8, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-3);

    private static readonly DateTime Now = new(2026, 8, 27, 6, 0, 0, DateTimeKind.Utc); // yerel 09:00

    [Fact]
    public void Gecerli_randevu_kabul_edilir() =>
        Assert.Null(Rules().Validate(Local(28, 11), Now));

    [Fact]
    public void Gecmis_tarih_reddedilir()
    {
        var problem = Rules().Validate(Local(26, 11), Now);
        Assert.Contains("Geçmiş", problem);
    }

    [Fact]
    public void Cok_yakin_randevu_reddedilir()
    {
        // Şu andan 10 dakika sonrası; varsayılan asgari mesafe 30 dakika.
        var problem = Rules().Validate(Now.AddMinutes(10), Now);
        Assert.Contains("30 dakika", problem);
    }

    [Fact]
    public void Asgari_mesafenin_tam_siniri_kabul_edilir() =>
        // 09:30 yerel, çalışma saati içinde ve tam 30 dakika sonrası.
        Assert.Null(Rules().Validate(Now.AddMinutes(30), Now));

    [Theory]
    [InlineData(8)]   // açılıştan önce
    [InlineData(18)]  // kapanışta başlayan randevu kapanıştan sonra biter
    [InlineData(23)]
    public void Mesai_disi_reddedilir(int localHour)
    {
        var problem = Rules().Validate(Local(28, localHour), Now);
        Assert.Contains("Randevu saatleri", problem);
    }

    [Fact]
    public void Kapanisa_tam_yetisen_randevu_kabul_edilir() =>
        // 17:30 + 30 dk = 18:00, tam sınırda.
        Assert.Null(Rules().Validate(Local(28, 17, 30), Now));

    [Fact]
    public void Acilis_saati_kabul_edilir() =>
        Assert.Null(Rules().Validate(Local(28, 9), Now));

    [Theory]
    [InlineData(29)] // Cumartesi
    [InlineData(30)] // Pazar
    public void Hafta_sonu_reddedilir(int day)
    {
        var problem = Rules().Validate(Local(day, 11), Now);
        Assert.Contains("çalışma günü", problem);
    }

    // Çalışma saatleri UTC'ye göre değil, yapılandırılan saat dilimine göre
    // değerlendirilmeli. Aynı UTC anı iki dilimde farklı sonuç vermeli.
    [Fact]
    public void Calisma_saatleri_saat_dilimine_gore_degerlendirilir()
    {
        var utcStart = new DateTime(2026, 8, 28, 7, 0, 0, DateTimeKind.Utc); // Istanbul 10:00

        var istanbul = new AppointmentRules(new AppointmentOptions(), Istanbul);
        Assert.Null(istanbul.Validate(utcStart, Now));

        // UTC-9'da aynı an 22:00 → mesai dışı.
        var honolulu = TimeZoneInfo.CreateCustomTimeZone("test-hst", TimeSpan.FromHours(-9), "HST", "HST");
        var other = new AppointmentRules(new AppointmentOptions(), honolulu);
        Assert.NotNull(other.Validate(utcStart, Now));
    }

    [Fact]
    public void Yapilandirma_calisma_saatlerini_degistirir()
    {
        var gece = new AppointmentOptions { WorkingHourStart = 0, WorkingHourEnd = 24 };
        Assert.Null(new AppointmentRules(gece, Istanbul).Validate(Local(28, 23), Now));
    }

    [Fact]
    public void Yapilandirma_calisma_gunlerini_degistirir()
    {
        var haftaSonu = new AppointmentOptions { WorkingDays = [0, 6] };
        Assert.Null(new AppointmentRules(haftaSonu, Istanbul).Validate(Local(29, 11), Now));
        Assert.NotNull(new AppointmentRules(haftaSonu, Istanbul).Validate(Local(28, 11), Now));
    }

    [Fact]
    public void Slot_yapilandirmadan_geliyor() =>
        Assert.Equal(TimeSpan.FromMinutes(45),
            new AppointmentRules(new AppointmentOptions { SlotMinutes = 45 }, Istanbul).Slot);

    // Saat dilimi bulunamazsa uygulama açılmamalı değil, UTC'ye düşmeli.
    [Fact]
    public void Bilinmeyen_saat_dilimi_UTC_ye_duser()
    {
        var rules = AppointmentRules.Create(new AppointmentOptions { TimeZone = "Yok/Boyle/Bir/Yer" });
        Assert.Null(rules.Validate(new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc), Now));
    }
}
