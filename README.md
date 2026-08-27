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
node hub-test.mjs      # SignalR: mesajlaşma + WebRTC sinyalleşme (backend çalışıyor olmalı)
node crypto-test.mjs   # Uçtan uca şifreleme (bağımsız, sunucu gerekmez)
node turn-test.mjs     # TURN kimlik bilgileri gerçekten çalışıyor mu
```

`hub-test.mjs` mesaj iletimini, veritabanına yazımı, yazıyor göstergesini, WebRTC el sıkışmasını ve görüşmenin taraflarına ait olmayan sinyallerin reddedildiğini doğrular.

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

TURN yapılandırması `frontend/.env.local` içinden gelir (`.env.example` dosyasını kopyalayın). Tanımlanmazsa yalnızca public STUN kullanılır ve doğrudan bağlanamayan kullanıcılar hata mesajı görür.

### Seçenek 1 — Metered (ayda 20 GB ücretsiz)

Kayıt gerekiyor; kayıtsız kullanılan eski `openrelay.metered.ca` ucu kapatılmıştır.

1. [metered.ca](https://www.metered.ca/stun-turn) üzerinden ücretsiz hesap açın
2. Panelden TURN kullanıcı adı ve şifresini alın
3. `frontend/.env.local` dosyasına yazın:

```bash
VITE_TURN_URLS=turn:global.relay.metered.ca:80,turn:global.relay.metered.ca:443
VITE_TURN_USERNAME=panelden-gelen-kullanici
VITE_TURN_CREDENTIAL=panelden-gelen-sifre
```

### Seçenek 2 — Yerel coturn (ücretsiz, yalnızca LAN)

Aynı ağdaki iki cihaz arasında test için yeterli; internet üzerinden görüşme için sunucunun genel IP'den erişilebilir olması gerekir.

```bash
TURN_EXTERNAL_IP=$(ipconfig getifaddr en0) docker compose --profile turn up -d
```

Kimlik bilgileri varsayılan olarak `hellodoctor` / `turn_dev_pw` (`TURN_USER`, `TURN_PASSWORD` ile değiştirilebilir).

### Doğrulama

Kimlik bilgilerini girdikten sonra gerçekten çalıştığını sınayın:

```bash
cd frontend && node turn-test.mjs
```

Sunucuya bir TURN `Allocate` isteği gönderip relay adresi alınabiliyor mu diye bakar. Yanlış şifre `401`, ulaşılamayan adres zaman aşımı verir — böylece tarayıcıda "arama kurulamadı" hatasının TURN'den mi başka bir katmandan mı geldiği belirsiz kalmaz.

Uçtan uca doğrulamak için doğrudan bağlantıyı tamamen kapatın:

```bash
VITE_ICE_TRANSPORT_POLICY=relay
```

Bu ayarla görüşme kurulabiliyorsa TURN gerçekten devrededir. Üretimde boş bırakın.

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

## Bilinen sınırlar

- WebRTC yalnızca `localhost` veya HTTPS üzerinde çalışır. Telefondan LAN IP'siyle test için HTTPS gerekir.
- TURN yapılandırılmadıysa doğrudan bağlanamayan kullanıcılar görüşemez (hata mesajı gösterilir).
- Veritabanı şifresi `appsettings.json` içinde geliştirme değeriyle duruyor; üretimde `ConnectionStrings__Postgres` ile geçersiz kılın.
- Giriş sınırı `RemoteIpAddress`'e göre bölümleniyor. Ters vekil sunucu arkasında tüm istekler vekilin IP'sinden görünür; o kurulumda `UseForwardedHeaders` yapılandırılmalı.
- `CallHub` bağlantı ve görüşme eşleşmelerini süreç belleğinde tutuyor — tek instance'a bağlı. Yatay ölçekleme için Redis backplane gerekir.

## Canlıya çıkmadan önce

Bu proje henüz üretime hazır değil. Kapatılmamış maddeler:

- **TURN kimlik bilgileri derlenmiş pakete gömülü.** Süreleri dolunca görüşmeler sessizce bozulur ve kullanıcıya yanlış sebep gösterilir; ayrıca kota herkese açık. Çözüm: kimlik bilgilerini backend'den çalışma anında veren bir uç nokta.
- **Randevu iş kuralları eksik.** Hasta kendi randevusunu onaylayabiliyor, geçmişe randevu alınabiliyor, çakışma kontrolü yok.
- **HTTPS zorlaması ve HSTS yok.** Yapılandırılmış log, health check ve yedekleme planı da yok.
- **Backend'de birim testi yok.** Yalnızca `hub-test.mjs` uçtan uca senaryosu var.
- **Görüşmenin gerçek cihazlarda çalıştığı doğrulanmadı.** Farklı ağlar arasında ses/görüntü akışı ve TURN'ün devreye girmesi henüz sınanmadı.
