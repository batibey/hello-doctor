# HelloDoctor 🩺

Hasta ve doktorları buluşturan mobil sağlık uygulaması: randevu, gerçek zamanlı mesajlaşma, sesli ve görüntülü görüşme.

## Mimari

| Katman | Teknoloji |
|---|---|
| Frontend | React 18 + Vite, React Router |
| Backend | ASP.NET Core 9 Web API |
| Veritabanı | PostgreSQL 16 + EF Core 9 (Npgsql) |
| Gerçek zamanlı | SignalR |
| Sesli/görüntülü | WebRTC (SignalR üzerinden sinyalleşme) |
| Kimlik doğrulama | JWT, PBKDF2 ile hash'lenmiş şifreler |
| Mesaj şifreleme | Uçtan uca: RSA-OAEP 2048 + AES-GCM 256 (WebCrypto) |
| E-posta | MailKit üzerinden SMTP (şifre sıfırlama) |

## Çalıştırma

Üç servis gerekiyor. Sırayla:

```bash
# 1. Veritabanı + yerel e-posta yakalayıcı (Mailpit: http://localhost:8025)
docker compose up -d

# 2. Backend  → http://localhost:5088
cd backend && dotnet run

# 3. Frontend → http://localhost:5173
cd frontend && npm install && npm run dev
```

Migration'lar ve demo verisi backend ilk açılışta otomatik uygulanır.

## Demo hesaplar

Tüm şifreler `1234`. Anahtar çiftleri sunucuda üretilemediği için (parolayı yalnızca istemci bilir) demo hesaplar anahtarsız tohumlanır; ilk girişte tarayıcı üretip yükler.

**Yalnızca `Development` ortamında tohumlanır** — şifreleri burada ve giriş ekranında yazılı olduğu için üretimde oluşturulmaları tüm sistemi açardı. Üretim derlemesinde giriş ekranındaki demo paneli de yer almaz, `/api/auth/demo-accounts` uç noktası `404` döner.

| Rol | E-posta |
|---|---|
| Hasta | `hasta@hellodoctor.com` |
| Hasta | `zeynep@hellodoctor.com` |
| Doktor | `dr.ayse@hellodoctor.com` (Kardiyoloji) |
| Doktor | `dr.mehmet@hellodoctor.com` (Dermatoloji) |
| Doktor | `dr.elif@hellodoctor.com` (Çocuk Sağlığı) |
| Doktor | `dr.canan@hellodoctor.com` (Psikiyatri) |

Mesajlaşma ve aramayı denemek için iki oturum açın: normal pencerede hasta, gizli pencerede doktor.

## Testler

```bash
cd frontend
node hub-test.mjs          # SignalR: mesajlaşma + WebRTC sinyalleşme
node appointment-test.mjs  # Randevu iş kuralları
node scaleout-test.mjs     # İki backend örneği arasında (Redis + 2 örnek gerekir)
node crypto-test.mjs       # Uçtan uca şifreleme (bağımsız, sunucu gerekmez)
node turn-test.mjs         # TURN kimlik bilgileri gerçekten çalışıyor mu
```

`hub-test.mjs` mesaj iletimini, veritabanına yazımı, yazıyor göstergesini, WebRTC el sıkışmasını ve görüşmenin taraflarına ait olmayan sinyallerin reddedildiğini doğrular.

`appointment-test.mjs` onay/iptal yetkilerini, çakışma kontrolünü ve çalışma saati kurallarını doğrular.

`scaleout-test.mjs` istemcileri **ayrı** backend örneklerine bağlar ve mesajlaşmanın, varlık bilgisinin, görüşme eşleşmesinin ve sinyal yetkilendirmesinin örnekler arasında çalıştığını doğrular. Kurulumu aşağıda.

`crypto-test.mjs` anahtar sarmalamayı, mesaj şifrelemeyi, yabancının çözemediğini ve şifre sıfırlandığında eski mesajların okunamaz hale geldiğini doğrular. Tarayıcı gerekmez — WebCrypto Node 18+ içinde var, uygulamanın kullandığı modülün aynısı çalışır.

## Yapılandırma

Ayarlar `backend/appsettings.json` içinde; her biri ortam değişkeniyle geçersiz kılınabilir. İç içe anahtarlar çift alt çizgi ile yazılır (`Jwt:Key` → `Jwt__Key`).

| Değişken | Açıklama |
|---|---|
| `Jwt__Key` | JWT imza anahtarı. **Üretimde zorunlu**, en az 32 bayt. |
| `Jwt__ExpiryDays` | Token ömrü (varsayılan 7) |
| `ConnectionStrings__Postgres` | Veritabanı bağlantı dizesi |
| `Cors__AllowedOrigins__0`, `__1`… | İzin verilen origin'ler. **Üretimde zorunlu.** |
| `RateLimit__LoginPerMinute` | IP başına dakikada giriş denemesi (varsayılan 5) |
| `ConnectionStrings__Redis` | Birden fazla örnek için. Boşken durum süreç belleğinde. |
| `Https__RedirectToHttps` | HTTP isteklerini HTTPS'e yönlendir (varsayılan açık, Development hariç) |
| `Https__HttpsPort` | Yönlendirmenin hedef portu (varsayılan 443) |
| `Https__HstsMaxAgeDays` | HSTS süresi (varsayılan 365) |
| `Https__HstsIncludeSubdomains`, `__HstsPreload` | HSTS kapsamı. **Preload geri alınamaz.** |
| `Https__UseForwardedHeaders` | **Ters vekil arkasındaysanız zorunlu.** |
| `Https__KnownProxies__0`, `__KnownNetworks__0` | Güvenilecek vekiller |
| `Appointments__SlotMinutes` | Randevu süresi, çakışma kontrolü buna göre (varsayılan 30) |
| `Appointments__TimeZone` | Çalışma saatlerinin değerlendirildiği dilim (varsayılan Europe/Istanbul) |
| `Appointments__WorkingHourStart`, `__WorkingHourEnd` | Çalışma saati aralığı (varsayılan 9–18) |
| `Appointments__WorkingDays__0`, `__1`… | 0 Pazar … 6 Cumartesi (varsayılan hafta içi) |
| `Appointments__MinimumNoticeMinutes` | Randevu en erken bu kadar sonrasına (varsayılan 30) |
| `Ice__MeteredApiKey` | Metered API anahtarı. **Asla appsettings.json'a yazmayın.** |
| `Ice__MeteredSubdomain` | Metered panelindeki alt alan adı |
| `Ice__TurnUrls__0`, `__1`… | Kendi TURN sunucunuz (Metered yerine) |
| `Ice__TurnUsername`, `Ice__TurnCredential` | Kendi TURN sunucunuzun kimlik bilgileri |
| `Ice__CacheMinutes` | TURN kimliğinin önbellek süresi (varsayılan 30) |
| `Ice__IceTransportPolicy` | `relay` yapılırsa doğrudan bağlantı denenmez (test için) |
| `Smtp__Host`, `Smtp__Port` | Şifre sıfırlama e-postası için SMTP sunucusu |
| `Smtp__User`, `Smtp__Password` | SMTP kimlik bilgileri (gerekiyorsa) |
| `Smtp__FromAddress` | Gönderen adresi |
| `Smtp__AppBaseUrl` | Sıfırlama bağlantısının işaret ettiği ön yüz adresi |

Geliştirmede anahtar `appsettings.Development.json` içinden gelir, ek kurulum gerekmez. Bu değer yalnızca yereldir ve üretimde kullanılmamalıdır.

Üretimde anahtar tanımsız veya 32 bayttan kısaysa uygulama **açılmayı reddeder** — zayıf anahtarla token imzalamaktansa erken hata vermeyi tercih eder. Aynı şey CORS için de geçerli: `Cors__AllowedOrigins` boşken üretimde açılmaz, çünkü her origin'e açık bir politika `AllowCredentials` ile birleşince herhangi bir sitenin kullanıcının tarayıcısı üzerinden kimlikli istek atmasına izin verirdi. Geliştirmede liste boş bırakılabilir; LAN IP'si ve tünel adresi sürekli değiştiği için orada serbesttir.

Giriş uç noktası IP başına dakikada 5 denemeyle sınırlı; aşan istek `429` ve `Retry-After` alır. `appsettings.Development.json` bu değeri 50'ye çekiyor, yoksa `hub-test.mjs` arka arkaya çalıştırıldığında kendi kendini kilitler.

```bash
export Jwt__Key="$(openssl rand -base64 48)"
export ConnectionStrings__Postgres="Host=…;Database=…;Username=…;Password=…"
export Cors__AllowedOrigins__0="https://hellodoctor.example"
dotnet run --no-launch-profile
```

## Hesap akışları

Kayıt (`/register`), şifremi unuttum (`/forgot-password`) ve sıfırlama (`/reset-password?token=…`) ekranları giriş ekranından bağlantılı.

Sıfırlama token'ı 1 saat geçerli, tek kullanımlık ve veritabanında yalnızca hash'i tutuluyor — veritabanı sızsa bile bağlantılar kullanılamaz. Yeni bir istek, bekleyen eski token'ları geçersiz kılıyor. `forgot-password` adresin kayıtlı olup olmadığına bakmaksızın aynı yanıtı veriyor; aksi halde bu uç nokta kimlerin üye olduğunu öğrenmek için kullanılabilirdi.

Geliştirmede e-postalar `docker compose` ile gelen **Mailpit**'e düşer: <http://localhost:8025>. Gerçek gönderim için `Smtp__*` değişkenlerini doldurun.

## Birden fazla örnekle çalıştırma

Varsayılan kurulum tek backend örneğine göredir: bağlantılar, varlık bilgisi ve görüşme eşleşmeleri süreç belleğinde tutulur. Geliştirme ve tek sunuculu kurulumlar ek altyapı istemesin diye böyle.

Yatay ölçekleme için Redis bağlayın:

```bash
docker compose up -d redis
export ConnectionStrings__Redis="localhost:6379"
```

Bu tanımlıyken iki şey birden devreye girer:

- **SignalR backplane** — hub mesajları örnekler arasında dağıtılır.
- **Paylaşılan görüşme durumu** — varlık (kim çevrimiçi) ve eşleşme (kim kiminle görüşüyor) Redis'te tutulur. `ICallStateStore` arkasında; Redis yoksa bellek içi uygulaması kullanılır.

Bağlantı listesi uygulamada hiç tutulmuyor: SignalR onu kendisi tutuyor ve `Clients.User(userId)` ile erişiliyor, backplane sayesinde örnekler arasında da çalışıyor.

Süreç çökerse bağlantı kaydı silinemeyeceği için varlık anahtarlarında 12 saatlik, eşleşme anahtarlarında 2 saatlik TTL var — kullanıcı sonsuza dek çevrimiçi ya da meşgul görünmesin.

Doğrulamak için iki örnek açıp `scaleout-test.mjs` çalıştırın:

```bash
ConnectionStrings__Redis=localhost:6379 dotnet run --no-launch-profile --urls http://localhost:5088
ConnectionStrings__Redis=localhost:6379 dotnet run --no-launch-profile --urls http://localhost:5090
cd frontend && node scaleout-test.mjs
```

## Log ve sağlık kontrolü

Geliştirmede okunabilir konsol logu, **üretimde satır başına tek JSON** — log toplayıcılar (Loki, CloudWatch, Datadog) alanlara göre sorgulayabilsin diye.

Her istek tek satır üretir: yöntem, yol, durum kodu, süre, kullanıcı kimliği, istemci IP'si ve iz kimliği. Durum koduna göre seviye ayarlanır (5xx hata, 4xx uyarı), böylece üretimde seviyeyi yükseltince gürültü değil gerçek sorunlar kalır.

**Sorgu dizesi bilerek loglanmaz.** SignalR JWT'yi `?access_token=` ile taşıyor (WebSocket'te `Authorization` başlığı gönderilemediği için) ve şifre sıfırlama token'ı da sorgu dizesinde geliyor; bunlar loga düşerse log dosyasını gören herkes oturum ele geçirebilir.

Log satırı yetkilendirmeden **önce** üretilir, yoksa `401` alan istekler hiç loglanmazdı — başarısız kimlik denemeleri tam da görülmesi gereken şey. Kullanıcı kimliği yine de yazılır, çünkü satır boru hattı geri sarılırken oluşturulur.

### Uçlar

| Uç | Neyi söyler |
|---|---|
| `GET /health/live` | Süreç ayakta mı. Bağımlılıklara bakmaz — başarısızsa konteyner yeniden başlatılmalı. |
| `GET /health/ready` | İstek karşılayabilir mi. Veritabanına bakar — başarısızsa trafik kesilmeli ama süreç öldürülmemeli. |

Ayrım önemli: veritabanı geçici düştüğünde uygulamayı yeniden başlatmak işe yaramaz, o yüzden `live` veritabanına bakmaz.

```bash
curl localhost:5088/health/ready
# {"status":"Healthy","checks":[{"name":"postgres","status":"Healthy"}]}
```

Uçlar kimlik doğrulaması istemez (orkestratör token taşıyamaz), bu yüzden gövde yalnızca kontrol adı ve durumu içerir — istisna metni ve süre bilgisi dışarı verilmez. `/health` istekleri kendi loglarını üretmez, saniyede bir çağrıldıkları için logu boğarlardı.

## HTTPS ve HSTS

Development dışında HTTP istekleri HTTPS'e yönlendirilir ve `Strict-Transport-Security` başlığı eklenir. Geliştirmede kapalıdır — Vite proxy'si `http://localhost:5088`'e gidiyor, açık olsa yerel geliştirme kırılırdı.

### Ters vekil arkasındaysanız

nginx, Caddy, ALB veya Cloudflare TLS'i sonlandırıp uygulamaya düz HTTP iletir. Bu durumda `Https__UseForwardedHeaders=true` **şart**:

```bash
export Https__UseForwardedHeaders=true
export Https__KnownProxies__0="10.0.0.5"      # vekilin IP'si
# ya da ağ olarak:
export Https__KnownNetworks__0="10.0.0.0/8"
```

Açmazsanız uygulama isteği HTTP sanar ve vekil ile uygulama arasında **sonsuz yönlendirme döngüsü** oluşur.

Güvenilecek vekili daraltmak önemli: liste boş bırakılırsa yalnızca loopback güvenilir, ama herhangi bir kaynağa güvenilirse istemci `X-Forwarded-For` uydurup giriş hız sınırını IP taklidiyle aşabilir. Bu ayar aynı zamanda hız sınırının gerçek istemci IP'sini görmesini sağlar.

### Preload uyarısı

`HstsPreload` pratikte geri alınamaz: tarayıcılar alan adını gömülü listeyle dağıtır ve listeden çıkmak aylar sürer. Alan adının tüm alt alanları kalıcı olarak HTTPS'e bağlanacaksa açın.

## Randevu kuralları

- **Onay doktorda.** Hasta randevu talebi oluşturur, durum `Pending` başlar; yalnızca doktor `Confirmed` yapabilir.
- **İptal her iki tarafta.** Hasta da doktor da iptal edebilir.
- **Tamamlandı doktorda.** Görüşmeyi tamamlandı işaretlemek klinik bir kayıt olduğu için doktorun yetkisinde ve randevunun önce onaylanmış olması gerekir.
- **Sonlanmış randevu değişmez.** `Completed` ve `Cancelled` uçtur; geri açılamaz.
- **Çakışma engellenir.** Aynı doktorun dolu saatine ikinci randevu alınamaz; hasta da aynı saate iki randevu alamaz. İptal edilen randevunun saati yeniden serbest kalır.
- **Geçmişe ve çok yakına randevu alınamaz**, çalışma günü ve saati dışına da.

Bunların hepsi sunucuda uygulanıyor; arayüzdeki düğmeler yalnızca izin verilen işlemleri gösteriyor ve reddedilen bir geçiş kullanıcıya sebebiyle bildiriliyor.

## Uçtan uca şifreleme

Mesajlar cihazda şifrelenir; **sunucu içeriği okuyamaz.** Sohbet ekranındaki "uçtan uca güvenli" ifadesi bu yüzden doğrudur.

Nasıl çalışıyor:

- Her kullanıcının RSA-OAEP 2048 anahtar çifti var. Açık anahtar sunucuda herkese açık durur.
- Özel anahtar, kullanıcının parolasından PBKDF2 (210.000 tur) ile türetilen bir AES-GCM anahtarıyla **sarmalanmış** olarak saklanır. Sunucu sarmalıyı görür ama açamaz.
- Her mesaj rastgele bir AES-GCM anahtarıyla şifrelenir; o anahtar hem alıcının hem gönderenin açık anahtarıyla ayrı ayrı şifrelenir, böylece gönderen de kendi yazdığını okuyabilir.
- Cihazda özel anahtar IndexedDB'de `extractable: false` olarak durur — sayfada XSS olsa bile dışarı çıkarılamaz, yalnızca kullanılabilir.

**Ham parola sunucuya hiç gitmez.** İstemci kimlik doğrulama için parolanın ayrı bir türevini gönderir (`frontend/src/crypto/keys.js` → `authVerifier`, karşılığı `backend/Services/AuthVerifier.cs`). Sunucu ham parolayı görseydi aynı sarmalama anahtarını türetip özel anahtarı açabilir, "sunucu okuyamaz" iddiası anlamını yitirirdi. İki taraf birebir aynı dizeyi üretmeli.

### Sonuçları

- **Şifre sıfırlamak eski mesajları okunamaz hale getirir.** Eski parola bilinmediği için eski özel anahtar açılamaz; sıfırlamada yeni bir çift üretilir. Kullanıcı bu konuda kayıt ekranında, sıfırlama ekranında ve e-postada uyarılıyor. Açılamayan mesajlar sohbette bunu belirten bir metinle görünür.
- **Sohbet listesindeki son mesaj önizlemesi sunucuda üretilemez**, istemcide çözülür.
- **Karşı tarafın açık anahtarı yoksa mesaj gönderilemez.** Sessizce düz metne düşmek yerine engelleniyor; kullanıcı şifreli sandığı bir mesajı açıkta göndermesin.
- Şifreleme öncesi yazılmış mesajlar düz metin olarak durur ve öyle görünür (`Encrypted = false`).

## Sesli/görüntülü görüşme (WebRTC)

Görüşme trafiği doğrudan hasta ile doktor arasında akar; sunucu yalnızca bağlantı kurulumunu (offer/answer/ICE) SignalR üzerinden taşır.

Sunucu bu sinyalleri körlemesine iletmez: `CallUser` ile kurulan çifti kaydeder ve offer/answer/ICE/kabul/ret/bitir mesajlarını yalnızca o çiftin iki tarafı arasında taşır. İstemci de gelen her sinyalin görüştüğü kişiden geldiğini ayrıca doğrular. Aksi halde bir kullanıcı ID'sini bilen üçüncü bir kişi görüşmeye teklif sokabilir ya da görüşmeyi düşürebilirdi.

Bazı ağlar doğrudan bağlantıya izin vermez — simetrik NAT, sıkı kurumsal güvenlik duvarları, bazı mobil operatörler. Bu durumda trafiği aktaran bir **TURN** sunucusu gerekir; pratikte görüşmelerin yaklaşık %10-20'si bunu gerektirir.

### ICE yapılandırması sunucudan gelir

İstemci TURN kimlik bilgilerini **çalışma anında** `GET /api/ice` üzerinden alır (kimlik doğrulaması gerekir). Sunucu bunları önbelleğe alır; süresi dolmadan tazelenir ve her aramadan önce kontrol edilir.

Bu uç nokta eklenmeden önce kimlik bilgileri `VITE_TURN_*` olarak derleme sırasında pakete gömülüyordu. Kazanç şu:

- **API anahtarı artık yalnızca sunucuda.** Anahtarla istenildiği kadar yeni TURN kimliği üretilebildiği için pakete girmesi en kötüsüydü.
- **Kimlik bilgisini değiştirmek yeniden derleme gerektirmiyor.** Panelden yenile, sunucu önbelleği dolunca yeni değeri dağıtır; ön yüze dokunmak gerekmez.
- **TURN kimliği paketten okunup kotanız harcanamaz.**

Dürüst olmak gerekirse son madde kısmi bir kazanç: tarayıcıya inen kimlik bilgisi ağ trafiğinden yine görülebilir — bu, tarayıcıda TURN kullanmanın doğasında var. Metered panelinden verilen kimlik varsayılan olarak **kalıcıdır**, kendiliğinden sona ermez. Gerçekten kısa ömürlü kimlik isterseniz Metered'ın "auto-expiring credentials" API'si var; `IceServerProvider` içindeki `FetchMeteredAsync` o uca çevrilirse yapı geri kalanıyla uyumlu çalışır.

Yapılandırma yoksa yalnızca public STUN kullanılır; doğrudan bağlanamayan kullanıcılar bunu söyleyen bir hata görür.

### Seçenek 1 — Metered (ayda 20 GB ücretsiz)

Kayıt gerekiyor; kayıtsız kullanılan eski `openrelay.metered.ca` ucu kapatılmıştır.

1. [metered.ca](https://www.metered.ca/stun-turn) üzerinden ücretsiz hesap açın
2. Panelden **"Show API Key"** ile API anahtarını alın (TURN kullanıcı/şifresini değil — sunucu onları anahtarla kendisi üretir)
3. Anahtarı ortam değişkeni olarak verin:

```bash
export Ice__MeteredApiKey="panelden-gelen-api-anahtari"
export Ice__MeteredSubdomain="hidoctor"   # panelinizdeki alt alan adı
```

Anahtarı `appsettings.json` içine **yazmayın**; o dosya git'e giriyor.

### Seçenek 2 — Kendi TURN sunucunuz (coturn)

Aynı ağdaki iki cihaz arasında test için yeterli; internet üzerinden görüşme için sunucunun genel IP'den erişilebilir olması gerekir.

```bash
TURN_EXTERNAL_IP=$(ipconfig getifaddr en0) docker compose --profile turn up -d

export Ice__TurnUrls__0="turn:192.168.1.101:3478"   # makinenizin LAN IP'si
export Ice__TurnUsername="hellodoctor"
export Ice__TurnCredential="turn_dev_pw"
```

Konteyner kimlik bilgileri varsayılan olarak `hellodoctor` / `turn_dev_pw` (`TURN_USER`, `TURN_PASSWORD` ile değiştirilebilir).

### Doğrulama

```bash
cd frontend && node turn-test.mjs
```

Kimlik bilgilerini `/api/ice`'tan çeker — yani tarayıcının gerçekte kullanacağı değerleri sınar — ve TURN sunucusuna bir `Allocate` isteği gönderip relay adresi alınabiliyor mu diye bakar. Yanlış şifre `401`, ulaşılamayan adres zaman aşımı verir; böylece tarayıcıdaki "arama kurulamadı" hatasının TURN'den mi başka bir katmandan mı geldiği belirsiz kalmaz.

Backend çalışmıyorsa doğrudan da verilebilir:

```bash
node turn-test.mjs turn:host:3478 kullanici sifre
```

Uçtan uca doğrulamak için doğrudan bağlantıyı tamamen kapatın:

```bash
export Ice__IceTransportPolicy=relay
```

Bu ayarla görüşme kurulabiliyorsa TURN gerçekten devrededir. Üretimde `all` kalmalı.

### Hata durumları

Bağlantı kurulamadığında arama ekranı sessizce takılmaz; nedeni belirten bir mesaj gösterir: izin reddi, cihaz bulunamaması, yanıt verilmemesi (45 sn), bağlantı zaman aşımı (30 sn) ve ICE başarısızlığı ayrı ayrı ele alınır.

Zilin hiç çalmayacağı durumlar zaman aşımı beklenmeden, aramanın ilk anında bildirilir: karşı taraf çevrimdışıysa, başka bir görüşmedeyse ya da aramayı yanıtlayamadıysa. Görüşme sırasında karşı tarafın bağlantısı tamamen koparsa sunucu diğer tarafa haber verir; kimse "Bağlanıyor…" ekranında 30 saniye beklemez.

## Veritabanı

Bağlantı dizesi `backend/appsettings.json` içinde. Konteyner **5433** portunda çalışır (yerel bir Postgres ile çakışmaması için).

```bash
# Şemayı incele
docker exec -it hellodoctor-db psql -U hellodoctor -d hellodoctor

# Sıfırdan başlat
docker compose down -v && docker compose up -d
```

### Şema

- **Users** — hasta ve doktor tek tabloda, `Role` ayrımıyla. `Email` benzersiz.
- **Appointments** — `PatientId`/`DoctorId` yabancı anahtar, silme `Restrict`.
- **Messages** — `ConversationId` iki kullanıcı ID'sinin sıralı birleşimi (`a__b`), sohbet sorgusu bu alan üzerinden indeksli.

Sorgu desenlerine göre indeksler: `(ConversationId, SentAt)` sohbet açılışı, `(RecipientId, Read)` okunmamış sayacı, `(PatientId, ScheduledAt)` randevu listesi.

Sohbet listesi (`GET /api/messages/conversations`) gruplamayı veritabanında yapar: `GROUP BY ConversationId` ile sohbet başına son mesaj zamanı ve okunmamış sayısı tek geçişte çıkar, ardından o son mesajlar `(ConversationId, SentAt)` indeksinden çekilir.

"Son mesaj"ı `NOT EXISTS (daha yeni mesaj yok)` ile seçmek daha doğal görünüyor ama ölçünce tuzak olduğu görüldü: eşitlik dışındaki `SentAt >` koşulu hash'lenemediği için Postgres anti join'e düşüyor ve tek bir yoğun sohbette karşılaştırma sayısı kareyle büyüyor. 5000 mesajlık bir sohbette bu sürüm 880 ms, `GROUP BY` sürümü 14 ms sürdü.

## Yedekleme ve geri yükleme

```bash
./scripts/backup.sh                    # backups/ altına, doğrulanmış dump
./scripts/restore.sh --latest          # en son yedekten geri yükle
./scripts/restore.sh backups/x.dump    # belirli bir yedekten
```

Yedek alındıktan sonra `pg_restore --list` ile okunabilirliği doğrulanır ve dosya ancak o zaman adını alır — yarım kalmış bir dump'ın geçerli yedek sanılması, yedeğin hiç olmamasından kötüdür.

Geri yükleme, üzerine yazmadan önce mevcut durumun yedeğini `backups/pre-restore-*.dump` olarak alır; yanlış dosya seçildiğinde dönüş yolu kalsın diye. Sonrasında backend yeniden başlatılmalı, havuzdaki bağlantılar koptuğu için.

### Şifreleme yedeklemeyi nasıl etkiliyor

**Mesajların okunabilirliği `Users` tablosundaki anahtar sütunlarına bağlı.** `WrappedPrivateKey`, `KeyWrapSalt` ve `KeyWrapIv` kaybolursa `Messages` tablosu eksiksiz geri gelse bile içerik kalıcı olarak açılamaz — sunucuda çözebilecek bir anahtar yok. Bu yüzden **tablo bazlı kısmi geri yükleme yapmayın**; yedeklemenin ve geri yüklemenin birimi tüm veritabanıdır.

Yukarıdaki betikler bunu sağlıyor. Geri yükleme sonrası şifreli bir mesajın hâlâ çözülebildiği sınandı.

### Redis yedeklenmez

Yalnızca varlık ve görüşme eşleşmesi tutuyor; ikisi de geçici. Kaybolursa o anda süren görüşmeler düşer, kalıcı veri kaybı olmaz.

### Zamanlama

```cron
0 3 * * * cd /opt/hellodoctor && ./scripts/backup.sh >> /var/log/hellodoctor-backup.log 2>&1
```

`RETENTION_DAYS` (varsayılan 14) eski yedekleri budar, `BACKUP_DIR` hedefi değiştirir.

### Üretim için eksikler

Betikler yerel dosyaya yazıyor. Gerçek bir kurulumda ayrıca gerekenler:

- **Yedekler makine dışına alınmalı.** Sunucuyla aynı diskte duran yedek, disk arızasında yedek değildir.
- **Yedekler şifrelenmeli.** Dump; e-posta, ad ve randevu bilgisi içeriyor (mesaj içerikleri şifreli, ama üst veri değil). Sağlık verisi için beklenen budur. Bu katman bilerek eklenmedi — anahtar yönetimi kurulumunuza bağlı ve yarım bir çözüm koymaktansa açıkça belirtmek daha doğru.
- **Kurtarma noktası hedefi.** Günlük dump, son yedekten sonraki 24 saate kadar veri kaybı demek. Daha kısası için WAL arşivleme ile point-in-time recovery gerekir.
- **Geri yükleme tatbikatı.** Denenmemiş yedek yedek değildir; düzenli aralıkla ayrı bir veritabanına geri yükleyip doğrulayın.

## Bilinen sınırlar

- WebRTC yalnızca `localhost` veya HTTPS üzerinde çalışır. Telefondan LAN IP'siyle test için HTTPS gerekir.
- TURN yapılandırılmadıysa doğrudan bağlanamayan kullanıcılar görüşemez (hata mesajı gösterilir).
- Veritabanı şifresi `appsettings.json` içinde geliştirme değeriyle duruyor; üretimde `ConnectionStrings__Postgres` ile geçersiz kılın.
- Giriş sınırı `RemoteIpAddress`'e göre bölümleniyor. Ters vekil arkasında doğru çalışması için `Https__UseForwardedHeaders` açılmalı (bkz. HTTPS ve HSTS).
- Varlık bilgisi anlık değil: bir kullanıcı yeniden bağlanırken (ağ kesintisi, sekme yenileme) kaydı silinip yeniden yazılana kadar kısa bir süre çevrimdışı görünebilir. Bu aralıkta gelen arama "kullanıcı çevrimiçi değil" alır; tekrar denemek yeterli.

## Canlıya çıkmadan önce

Bu proje henüz üretime hazır değil. Kapatılmamış maddeler:

- **Backend'de birim testi yok.** Yalnızca `hub-test.mjs` uçtan uca senaryosu var.
- **Görüşmenin gerçek cihazlarda çalıştığı doğrulanmadı.** Farklı ağlar arasında ses/görüntü akışı ve TURN'ün devreye girmesi henüz sınanmadı.
