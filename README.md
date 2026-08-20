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
- STUN sunucusu public Google STUN. Simetrik NAT arkasında bağlantı için TURN sunucusu eklenmelidir.
- JWT imzalama anahtarı `TokenService.cs` içinde sabit — canlıya çıkmadan önce ortam değişkenine taşınmalı.
