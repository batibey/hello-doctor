// Mevzuat kaynaklı kuralları API üzerinden doğrular: hekim doğrulaması,
// rıza zorunluluğu, denetim kaydı ve KVKK veri sahibi hakları.
//
// Çalıştır: node compliance-test.mjs   (backend çalışıyor olmalı)
import { createHash, webcrypto } from 'node:crypto'

if (!globalThis.crypto) globalThis.crypto = webcrypto

const API = process.env.HD_API || 'http://localhost:5088'
const verifier = (p) => createHash('sha256').update(`hellodoctor:auth:v1:${p}`).digest('base64')

const call = async (token, method, path, body) => {
  const r = await fetch(`${API}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  let data = null
  try { data = await r.json() } catch { /* gövdesiz */ }
  return { status: r.status, data }
}

const login = async (email, role, password = '1234') => {
  const r = await call(null, 'POST', '/api/auth/login',
    { email, password: verifier(password), role })
  if (r.status !== 200) throw new Error(`login ${email}: ${r.status}`)
  return r.data
}

// Gerçek anahtar üretmek yavaş; kurallar anahtarın içeriğine bakmıyor.
const dummyKeys = {
  publicKey: 'AAAA', wrappedPrivateKey: 'BBBB', keyWrapSalt: 'CCCC', keyWrapIv: 'DDDD',
}

const results = []
const check = (name, ok, detail = '') => {
  results.push(ok)
  console.log(`${ok ? '✅' : '❌'} ${name}${detail ? ` — ${detail}` : ''}`)
}

const run = async () => {
  const stamp = Date.now()
  const admin = await login('hasta@hellodoctor.com', 'Patient')   // tohumlamada yönetici
  const patient = await login('zeynep@hellodoctor.com', 'Patient')
  check('Yönetici ve hasta giriş yaptı', !!admin.token && !!patient.token)

  // --- Rıza zorunluluğu ---
  const base = {
    email: `riza${stamp}@ornek.com`, password: verifier('SifreTest123'),
    fullName: 'Rıza Testi', role: 'Patient', ...dummyKeys,
  }
  let r = await call(null, 'POST', '/api/auth/register',
    { ...base, acceptedPrivacyNotice: false, acceptedHealthDataConsent: true })
  check('Aydınlatma onayı olmadan kayıt reddedildi', r.status === 400, r.data?.message)

  r = await call(null, 'POST', '/api/auth/register',
    { ...base, acceptedPrivacyNotice: true, acceptedHealthDataConsent: false })
  check('Açık rıza olmadan kayıt reddedildi', r.status === 400, r.data?.message)

  r = await call(null, 'POST', '/api/auth/register',
    { ...base, acceptedPrivacyNotice: true, acceptedHealthDataConsent: true })
  check('Onaylarla kayıt kabul edildi', r.status === 200)
  const newPatient = r.data

  const consents = await call(newPatient.token, 'GET', '/api/privacy/consents')
  check('Rıza kayıtları tutuldu',
    consents.data?.length === 2 && consents.data.every((c) => c.granted),
    consents.data?.map((c) => `${c.documentKey} v${c.version}`).join(', '))

  // --- Hekim doğrulaması ---
  const docBase = {
    email: `hekim${stamp}@ornek.com`, password: verifier('SifreTest123'),
    fullName: 'Doğrulanmamış Hekim', role: 'Doctor', specialty: 'Test',
    acceptedPrivacyNotice: true, acceptedHealthDataConsent: true, ...dummyKeys,
  }
  r = await call(null, 'POST', '/api/auth/register', docBase)
  check('Tescil numarasız hekim kaydı reddedildi', r.status === 400, r.data?.message)

  r = await call(null, 'POST', '/api/auth/register',
    { ...docBase, medicalLicenseNumber: `TEST-${stamp}` })
  check('Hekim kaydı onaya düştü',
    r.status === 200 && r.data?.user?.verification === 'Pending',
    r.data?.user?.verification)
  const doctor = r.data

  // Doğrulanmamış hekim hastaya görünmemeli
  const list = await call(patient.token, 'GET', '/api/users/doctors')
  check('Doğrulanmamış hekim doktor listesinde yok',
    !list.data.some((d) => d.id === doctor.user.id), `${list.data.length} hekim listelendi`)

  // Doğrulanmamış hekimden randevu alınamamalı
  // Hafta içi bir gün seç: çalışma günü kuralı hafta sonunu reddediyor.
  const slot = new Date(Date.now() + 3 * 86400000)
  slot.setUTCHours(8, 0, 0, 0)   // Istanbul 11:00
  while ([0, 6].includes(slot.getUTCDay())) slot.setUTCDate(slot.getUTCDate() + 1)
  r = await call(patient.token, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: slot.toISOString(), type: 'Video' })
  check('Doğrulanmamış hekimden randevu alınamadı', r.status === 400, r.data?.message)

  // Yönetici olmayan doğrulama yapamamalı
  r = await call(patient.token, 'POST', `/api/admin/doctors/${doctor.user.id}/verify`,
    { decision: 'verified', note: null })
  check('Yönetici olmayan doğrulama yapamadı', r.status === 403, `HTTP ${r.status}`)

  // Bekleyenler listesi
  r = await call(admin.token, 'GET', '/api/admin/doctors/pending')
  check('Bekleyen hekim yönetici listesinde',
    r.data.some((d) => d.id === doctor.user.id))

  // Ret gerekçesi zorunlu
  r = await call(admin.token, 'POST', `/api/admin/doctors/${doctor.user.id}/verify`,
    { decision: 'rejected', note: '' })
  check('Ret gerekçesi zorunlu', r.status === 400, r.data?.message)

  // Onay
  r = await call(admin.token, 'POST', `/api/admin/doctors/${doctor.user.id}/verify`,
    { decision: 'verified', note: 'Test doğrulaması' })
  check('Yönetici hekimi doğruladı', r.status === 200 && r.data?.verification === 'Verified')

  const list2 = await call(patient.token, 'GET', '/api/users/doctors')
  check('Doğrulanan hekim listede görünüyor',
    list2.data.some((d) => d.id === doctor.user.id))

  r = await call(patient.token, 'POST', '/api/appointments',
    { doctorId: doctor.user.id, scheduledAt: slot.toISOString(), type: 'Video' })
  check('Doğrulanan hekimden randevu alınabildi', r.status === 200, r.data?.status)
  const apptId = r.data?.id

  // --- Denetim kaydı ---
  await call(patient.token, 'GET', `/api/users/${doctor.user.id}`)
  await call(patient.token, 'GET', `/api/messages/${doctor.user.id}`)

  const doctorLog = await call(doctor.token, 'GET', '/api/admin/access-log')
  check('Hekimin verisine erişim kaydedildi',
    doctorLog.data.some((e) => e.actorId === patient.user.id),
    `${doctorLog.data.length} kayıt`)

  const own = await call(patient.token, 'GET', `/api/admin/access-log?subjectId=${doctor.user.id}`)
  check('Yabancının denetim kaydına erişilemedi', own.status === 403, `HTTP ${own.status}`)

  // --- KVKK hakları ---
  const exported = await call(newPatient.token, 'GET', '/api/privacy/export')
  check('Veri dışa aktarma çalışıyor',
    exported.status === 200 && !!exported.data?.kullanici && Array.isArray(exported.data?.rizalar),
    `${Object.keys(exported.data || {}).length} bölüm`)

  r = await call(newPatient.token, 'POST', '/api/privacy/delete-account', { confirmation: 'yanlış' })
  check('Yanlış onay metniyle silme reddedildi', r.status === 400, r.data?.message)

  r = await call(newPatient.token, 'POST', '/api/privacy/delete-account', { confirmation: 'HESABIMI SİL' })
  check('Hesap silindi ve anahtar yok edildi', r.status === 200, r.data?.message?.slice(0, 40))

  const me = await call(newPatient.token, 'GET', '/api/users/me')
  check('Silinen hesap anonimleştirildi',
    me.data?.fullName === 'Silinmiş kullanıcı' && !me.data?.publicKey, me.data?.fullName)

  // Test randevusunu iptal ederek bırak
  if (apptId) await call(patient.token, 'PUT', `/api/appointments/${apptId}/status`, { status: 'Cancelled' })

  const failed = results.filter((x) => !x).length
  console.log(`\n${results.length - failed}/${results.length} test geçti`)
  process.exit(failed ? 1 : 0)
}

run().catch((e) => { console.error('❌ HATA:', e.message); process.exit(1) })
