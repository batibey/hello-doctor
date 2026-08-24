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

## Çalıştırma

Üç servis gerekiyor. Sırayla:

```bash
# 1. Veritabanı
docker compose up -d

# 2. Backend  → http://localhost:5088
cd backend && dotnet run

# 3. Frontend → http://localhost:5173
cd frontend && npm install && npm run dev
```

Migration'lar ve demo verisi backend ilk açılışta otomatik uygulanır.

## Demo hesaplar

Tüm şifreler `1234`.

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
cd frontend && node hub-test.mjs
```

SignalR üzerinden mesaj iletimi, veritabanına yazım, yazıyor göstergesi ve WebRTC sinyalleşme el sıkışmasını uçtan uca doğrular.

## Yapılandırma

Ayarlar `backend/appsettings.json` içinde; her biri ortam değişkeniyle geçersiz kılınabilir. İç içe anahtarlar çift alt çizgi ile yazılır (`Jwt:Key` → `Jwt__Key`).

| Değişken | Açıklama |
|---|---|
| `Jwt__Key` | JWT imza anahtarı. **Üretimde zorunlu**, en az 32 bayt. |
| `Jwt__ExpiryDays` | Token ömrü (varsayılan 7) |
| `ConnectionStrings__Postgres` | Veritabanı bağlantı dizesi |

Geliştirmede anahtar `appsettings.Development.json` içinden gelir, ek kurulum gerekmez. Bu değer yalnızca yereldir ve üretimde kullanılmamalıdır.

Üretimde anahtar tanımsız veya 32 bayttan kısaysa uygulama **açılmayı reddeder** — zayıf anahtarla token imzalamaktansa erken hata vermeyi tercih eder.

```bash
export Jwt__Key="$(openssl rand -base64 48)"
export ConnectionStrings__Postgres="Host=…;Database=…;Username=…;Password=…"
dotnet run --no-launch-profile
```

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
