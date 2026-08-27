// Randevu iş kurallarını API üzerinden doğrular: kim onaylayabilir, kim iptal
// edebilir, çakışma ve çalışma saati kontrolleri.
//
// Çalıştır: node appointment-test.mjs   (backend çalışıyor olmalı)
import { createHash } from 'node:crypto'

const API = process.env.HD_API || 'http://localhost:5088'
const TZ = 'Europe/Istanbul'

const authVerifier = (p) =>
  createHash('sha256').update(`hellodoctor:auth:v1:${p}`).digest('base64')

const login = async (email, role) => {
  const r = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password: authVerifier('1234'), role }),
  })
  if (!r.ok) throw new Error(`login ${email}: ${r.status}`)
  return r.json()
}

const call = async (token, method, path, body) => {
  const r = await fetch(`${API}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body ? JSON.stringify(body) : undefined,
  })
  let data = null
  try { data = await r.json() } catch { /* gövdesiz yanıt */ }
  return { status: r.status, data }
}

const results = []
const check = (name, ok, detail = '') => {
  results.push(ok)
  console.log(`${ok ? '✅' : '❌'} ${name}${detail ? ` — ${detail}` : ''}`)
}

// Europe/Istanbul'da belirli bir saate denk gelen UTC anını üretir.
// Sunucu çalışma saatlerini o dilimde değerlendirdiği için test de öyle kurmalı.
function istanbulSlot({ daysAhead = 1, hour = 10, minute = 0 } = {}) {
  const now = new Date()
  const d = new Date(now.getTime() + daysAhead * 86400000)

  // Hafta sonuna denk gelirse pazartesiye kaydır (çalışma günü varsayılanı hafta içi).
  const wd = Number(new Intl.DateTimeFormat('en', { timeZone: TZ, weekday: 'short' })
    .format(d).replace(/Sun|Mon|Tue|Wed|Thu|Fri|Sat/, (m) =>
      ({ Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 })[m]))
  if (wd === 6) d.setTime(d.getTime() + 2 * 86400000)
  if (wd === 0) d.setTime(d.getTime() + 1 * 86400000)

  const ymd = new Intl.DateTimeFormat('en-CA', {
    timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(d)

  // Istanbul yıl boyu UTC+3 (yaz saati uygulaması yok).
  const hh = String(hour).padStart(2, '0')
  const mm = String(minute).padStart(2, '0')
  return new Date(`${ymd}T${hh}:${mm}:00+03:00`).toISOString()
}

const run = async () => {
  const patient = await login('hasta@hellodoctor.com', 'Patient')
  const other = await login('zeynep@hellodoctor.com', 'Patient')
  const doctor = await login('dr.mehmet@hellodoctor.com', 'Doctor')
  check('Hasta, ikinci hasta ve doktor giriş yaptı', true)

  const P = patient.token, O = other.token, D = doctor.token
  const created = []

  // --- 1. Geçmiş tarih ---
  const past = new Date(Date.now() - 86400000).toISOString()
  let r = await call(P, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: past, type: 'Video', reason: 'test' })
  check('Geçmiş tarihe randevu reddedildi', r.status === 400, r.data?.message)

  // --- 2. Çok yakın tarih ---
  const soon = new Date(Date.now() + 5 * 60000).toISOString()
  r = await call(P, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: soon, type: 'Video' })
  check('Çok yakın randevu reddedildi', r.status === 400, r.data?.message)

  // --- 3. Çalışma saati dışı ---
  r = await call(P, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: istanbulSlot({ hour: 23 }), type: 'Video' })
  check('Mesai dışı saat reddedildi', r.status === 400, r.data?.message)

  // --- 4. Hafta sonu ---
  // Önümüzdeki cumartesiyi bul.
  const sat = new Date()
  while (Number(new Intl.DateTimeFormat('en', { timeZone: TZ, weekday: 'short' })
    .format(sat).replace(/\w+/, (m) => ({ Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 })[m])) !== 6) {
    sat.setDate(sat.getDate() + 1)
  }
  const satYmd = new Intl.DateTimeFormat('en-CA', {
    timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(sat)
  r = await call(P, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: new Date(`${satYmd}T10:00:00+03:00`).toISOString(), type: 'Video' })
  check('Hafta sonu reddedildi', r.status === 400, r.data?.message)

  // --- 5. Geçerli randevu ---
  const slot = istanbulSlot({ daysAhead: 3, hour: 11, minute: 0 })
  r = await call(P, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: slot, type: 'Video', reason: 'Kural testi' })
  check('Geçerli randevu oluşturuldu', r.status === 200, r.data?.status)
  const appt = r.data
  if (appt?.id) created.push(appt.id)
  check('Başlangıç durumu Pending', appt?.status === 'Pending')

  // --- 6. Çakışma: aynı doktor, aynı saat ---
  r = await call(O, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: slot, type: 'Voice' })
  check('Dolu saate ikinci randevu reddedildi', r.status === 409, r.data?.message)

  // --- 7. Çakışma: slot içinde kayan saat ---
  const overlapping = new Date(Date.parse(slot) + 15 * 60000).toISOString()
  r = await call(O, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: overlapping, type: 'Voice' })
  check('Slot içine düşen saat de reddedildi', r.status === 409, r.data?.message)

  // --- 8. Bitişik slot serbest ---
  const adjacent = new Date(Date.parse(slot) + 30 * 60000).toISOString()
  r = await call(O, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: adjacent, type: 'Voice' })
  check('Bitişik slot kabul edildi', r.status === 200, r.data?.status)
  if (r.data?.id) created.push(r.data.id)

  // --- 9. Onay yalnızca doktorda ---
  r = await call(P, 'PUT', `/api/appointments/${appt.id}/status`, { status: 'Confirmed' })
  check('Hasta kendi randevusunu onaylayamadı', r.status === 400, r.data?.message)

  r = await call(D, 'PUT', `/api/appointments/${appt.id}/status`, { status: 'Confirmed' })
  check('Doktor onayladı', r.status === 200 && r.data?.status === 'Confirmed')

  // --- 10. Tamamlandı yalnızca doktorda ---
  r = await call(P, 'PUT', `/api/appointments/${appt.id}/status`, { status: 'Completed' })
  check('Hasta tamamlandı işaretleyemedi', r.status === 400, r.data?.message)

  // --- 11. İptal her iki tarafta ---
  r = await call(P, 'PUT', `/api/appointments/${appt.id}/status`, { status: 'Cancelled' })
  check('Hasta iptal edebildi', r.status === 200 && r.data?.status === 'Cancelled')

  // --- 12. Sonlanmış randevu değişmez ---
  r = await call(D, 'PUT', `/api/appointments/${appt.id}/status`, { status: 'Confirmed' })
  check('İptal edilmiş randevu geri açılamadı', r.status === 400, r.data?.message)

  // --- 13. İptal edilen saat yeniden alınabilir ---
  r = await call(O, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: slot, type: 'Voice' })
  check('İptal edilen saat serbest kaldı', r.status === 200)
  if (r.data?.id) created.push(r.data.id)

  // --- 14. Yabancı randevuya dokunamaz ---
  const stranger = await login('dr.canan@hellodoctor.com', 'Doctor')
  r = await call(stranger.token, 'PUT', `/api/appointments/${created[1]}/status`, { status: 'Cancelled' })
  check('Taraf olmayan kullanıcı reddedildi', r.status === 403 || r.status === 404, `HTTP ${r.status}`)

  // --- 15. Doktor randevu oluşturamaz ---
  r = await call(D, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: istanbulSlot({ daysAhead: 5 }), type: 'Video' })
  check('Doktor kendine randevu oluşturamadı', r.status === 403, `HTTP ${r.status}`)

  // Test kayıtlarını iptal ederek bırak; silme uç noktası yok.
  for (const id of created) {
    await call(D, 'PUT', `/api/appointments/${id}/status`, { status: 'Cancelled' })
  }

  const failed = results.filter((x) => !x).length
  console.log(`\n${results.length - failed}/${results.length} test geçti`)
  process.exit(failed ? 1 : 0)
}

run().catch((e) => { console.error('❌ HATA:', e.message); process.exit(1) })
