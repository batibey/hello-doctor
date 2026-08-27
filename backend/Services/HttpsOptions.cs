namespace HelloDoctor.Api.Services;

public class HttpsOptions
{
    public const string SectionName = "Https";

    // Geliştirmede kapalı: Vite proxy'si http://localhost:5088'e gidiyor,
    // yönlendirme açık olsa yerel geliştirme tamamen kırılırdı.
    public bool RedirectToHttps { get; set; } = true;

    // Yönlendirmenin hedef portu. Ters vekil arkasında dışarıya bakan port.
    public int HttpsPort { get; set; } = 443;

    // Strict-Transport-Security. Tarayıcı bu süre boyunca siteye yalnızca
    // HTTPS ile bağlanır — araya girip HTTP'ye düşürme saldırısını keser.
    public int HstsMaxAgeDays { get; set; } = 365;
    public bool HstsIncludeSubdomains { get; set; } = true;

    // DİKKAT: preload listesine girmek pratikte geri alınamaz; tarayıcılar
    // alan adını gömülü listeyle dağıtır ve çıkmak aylar sürer. Alan adının
    // tüm alt alanları kalıcı olarak HTTPS'e bağlanacaksa açın.
    public bool HstsPreload { get; set; } = false;

    // Ters vekil (nginx, Caddy, ALB, Cloudflare) arkasındaysanız açın.
    // Kapalıyken vekilin ilettiği X-Forwarded-* başlıkları yok sayılır;
    // uygulama isteği HTTP sanar ve HTTPS yönlendirmesi döngüye girer.
    public bool UseForwardedHeaders { get; set; } = false;

    // Hangi vekile güvenileceği. Boş bırakılırsa yalnızca loopback güvenilir.
    // Güvenilmeyen bir kaynaktan gelen X-Forwarded-For sahte olabilir; o yüzden
    // kapsam dar tutulmalı — aksi halde giriş hız sınırı IP taklidiyle aşılır.
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = []; // "10.0.0.0/8" biçiminde
}
