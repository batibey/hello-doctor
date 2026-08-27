namespace HelloDoctor.Api.Services;

public class IceOptions
{
    public const string SectionName = "Ice";

    // STUN yalnızca dış adresi bildirir, trafik taşımaz — public sunucular yeterli.
    public string[] StunUrls { get; set; } =
        ["stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302"];

    // Seçenek 1: Metered. API anahtarı sunucuda kalır, istemciye yalnızca
    // süreli kimlik bilgisi iner.
    public string MeteredSubdomain { get; set; } = "hidoctor";
    public string? MeteredApiKey { get; set; }

    // Seçenek 2: kendi TURN sunucunuz (ör. coturn). Sabit kimlik bilgisi.
    public string[] TurnUrls { get; set; } = [];
    public string? TurnUsername { get; set; }
    public string? TurnCredential { get; set; }

    // Metered'dan çekilen kimlik bilgileri bu süre boyunca yeniden kullanılır.
    // Sağlayıcı kimlikleri daha uzun ömürlü verir; buradaki değer yalnızca
    // her aramada dış servise gitmemek için.
    public int CacheMinutes { get; set; } = 30;

    // 'relay' yapılırsa doğrudan bağlantı hiç denenmez; TURN'ün gerçekten
    // çalıştığını doğrulamak için. Üretimde 'all' kalmalı.
    public string IceTransportPolicy { get; set; } = "all";

    public bool UsesMetered => !string.IsNullOrWhiteSpace(MeteredApiKey);
    public bool UsesStaticTurn => TurnUrls.Length > 0;
}
