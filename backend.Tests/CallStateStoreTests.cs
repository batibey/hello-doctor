using HelloDoctor.Api.Services;
using Xunit;

namespace HelloDoctor.Api.Tests;

// Bellek içi depo statik sözlükler kullanıyor (süreç boyunca tek örnek), bu
// yüzden testler birbirinin kullanıcılarını görmesin diye her test benzersiz
// kimlik üretiyor.
public class InMemoryCallStateStoreTests
{
    private readonly InMemoryCallStateStore _sut = new();
    private static string NewId() => Guid.NewGuid().ToString();

    [Fact]
    public async Task Baglantisi_olmayan_kullanici_cevrimdisi() =>
        Assert.False(await _sut.IsOnlineAsync(NewId()));

    [Fact]
    public async Task Baglanan_kullanici_cevrimici()
    {
        var user = NewId();
        await _sut.AddConnectionAsync(user, "conn-1");
        Assert.True(await _sut.IsOnlineAsync(user));
    }

    [Fact]
    public async Task Birden_fazla_sekme_son_kapanana_kadar_cevrimici()
    {
        var user = NewId();
        await _sut.AddConnectionAsync(user, "conn-1");
        await _sut.AddConnectionAsync(user, "conn-2");

        Assert.False(await _sut.RemoveConnectionAsync(user, "conn-1"));
        Assert.True(await _sut.IsOnlineAsync(user));

        Assert.True(await _sut.RemoveConnectionAsync(user, "conn-2"));
        Assert.False(await _sut.IsOnlineAsync(user));
    }

    [Fact]
    public async Task Bilinmeyen_baglanti_silmek_cokmez()
    {
        var user = NewId();
        Assert.True(await _sut.RemoveConnectionAsync(user, "hic-olmayan"));
    }

    [Fact]
    public async Task Hedefi_sahiplenme_ilk_arayana_verilir()
    {
        string target = NewId(), caller = NewId(), other = NewId();

        Assert.Equal(caller, await _sut.ClaimPeerAsync(target, caller));
        // İkinci arayan mevcut sahibi görür → meşgul.
        Assert.Equal(caller, await _sut.ClaimPeerAsync(target, other));
    }

    [Fact]
    public async Task Ayni_arayan_tekrar_deneyebilir()
    {
        string target = NewId(), caller = NewId();
        await _sut.ClaimPeerAsync(target, caller);
        Assert.Equal(caller, await _sut.ClaimPeerAsync(target, caller));
    }

    [Fact]
    public async Task Es_kaydi_okunabiliyor()
    {
        string a = NewId(), b = NewId();
        await _sut.SetPeerAsync(a, b);
        Assert.Equal(b, await _sut.GetPeerAsync(a));
        Assert.Null(await _sut.GetPeerAsync(NewId()));
    }

    [Fact]
    public async Task Bayat_kayit_yeni_aramayi_engellemez()
    {
        // Tarayıcı çökerse eski eş kaydı kalır; SetPeer üzerine yazmazsa
        // kullanıcı bir daha hiç arama yapamazdı.
        string a = NewId(), eski = NewId(), yeni = NewId();
        await _sut.SetPeerAsync(a, eski);
        await _sut.SetPeerAsync(a, yeni);
        Assert.Equal(yeni, await _sut.GetPeerAsync(a));
    }

    [Fact]
    public async Task Cift_temizlenince_iki_taraf_da_silinir()
    {
        string a = NewId(), b = NewId();
        await _sut.ClaimPeerAsync(b, a);
        await _sut.SetPeerAsync(a, b);

        await _sut.ClearPairAsync(a, b);

        Assert.Null(await _sut.GetPeerAsync(a));
        Assert.Null(await _sut.GetPeerAsync(b));
    }

    [Fact]
    public async Task Gecikmis_temizlik_yeni_gorusmeyi_bozmaz()
    {
        // a önce b ile görüşüyordu, sonra c ile yeni bir görüşme başladı.
        // b'den gecikmeli gelen EndCall, a-c çiftini bozmamalı.
        string a = NewId(), b = NewId(), c = NewId();
        await _sut.SetPeerAsync(a, c);
        await _sut.SetPeerAsync(c, a);

        await _sut.ClearPairAsync(a, b);

        Assert.Equal(c, await _sut.GetPeerAsync(a));
        Assert.Equal(a, await _sut.GetPeerAsync(c));
    }

    [Fact]
    public async Task Es_zamanli_sahiplenmede_tek_kazanan_olur()
    {
        var target = NewId();
        var callers = Enumerable.Range(0, 50).Select(_ => NewId()).ToArray();

        var winners = await Task.WhenAll(
            callers.Select(c => Task.Run(() => _sut.ClaimPeerAsync(target, c))));

        // Herkes aynı sahibi görmeli; sahip de arayanlardan biri olmalı.
        Assert.Single(winners.Distinct());
        Assert.Contains(winners[0], callers);
    }

    [Fact]
    public async Task Es_zamanli_baglanti_ekleme_kaybolmaz()
    {
        var user = NewId();
        await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => _sut.AddConnectionAsync(user, $"conn-{i}"))));

        Assert.True(await _sut.IsOnlineAsync(user));

        for (var i = 0; i < 99; i++)
            Assert.False(await _sut.RemoveConnectionAsync(user, $"conn-{i}"));

        Assert.True(await _sut.RemoveConnectionAsync(user, "conn-99"));
    }
}
