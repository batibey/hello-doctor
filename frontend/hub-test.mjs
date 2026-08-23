// Headless end-to-end check of the SignalR hub: real-time chat delivery,
// persistence, and the WebRTC signaling handshake. Run: node hub-test.mjs
import * as signalR from '@microsoft/signalr'

// Varsayılan olarak backend'e doğrudan bağlanır. Tünel veya LAN üzerinden
// sınamak için: HD_API=https://... node hub-test.mjs
const API = process.env.HD_API || 'http://localhost:5088'

const login = async (email, role) => {
  const r = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password: '1234', role }),
  })
  if (!r.ok) throw new Error(`login failed for ${email}: ${r.status}`)
  return r.json()
}

const connect = async (token) => {
  const c = new signalR.HubConnectionBuilder()
    .withUrl(`${API}/hubs/call?access_token=${token}`, {
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets,
    })
    .configureLogging(signalR.LogLevel.Error)
    .build()
  await c.start()
  return c
}

const waitFor = (conn, event, timeout = 5000) =>
  new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error(`timeout waiting for ${event}`)), timeout)
    conn.on(event, (...args) => { clearTimeout(t); resolve(args) })
  })

const results = []
const check = (name, ok, detail = '') => {
  results.push({ name, ok, detail })
  console.log(`${ok ? '✅' : '❌'} ${name}${detail ? ` — ${detail}` : ''}`)
}

const run = async () => {
  const patient = await login('hasta@hellodoctor.com', 'Patient')
  const doctor = await login('dr.elif@hellodoctor.com', 'Doctor')
  check('İki kullanıcı giriş yaptı', true, `${patient.user.fullName} + ${doctor.user.fullName}`)

  const pc = await connect(patient.token)
  const dc = await connect(doctor.token)
  check('Her iki SignalR bağlantısı kuruldu', true)

  // --- 1. Real-time chat delivery ---
  const text = `Hub testi ${Date.now()}`
  const incoming = waitFor(dc, 'ReceiveMessage')
  await pc.invoke('SendMessage', doctor.user.id, text)
  const [msg] = await incoming
  check('Mesaj doktora anında ulaştı', msg.text === text, `"${msg.text}"`)

  // --- 2. Persisted to Postgres (hub uses IDbContextFactory) ---
  const thread = await fetch(`${API}/api/messages/${patient.user.id}`, {
    headers: { Authorization: `Bearer ${doctor.token}` },
  }).then((r) => r.json())
  check('Mesaj veritabanına yazıldı', thread.some((m) => m.text === text),
    `${thread.length} mesaj bulundu`)

  // --- 3. Typing indicator ---
  const typing = waitFor(dc, 'Typing')
  await pc.invoke('Typing', doctor.user.id, true)
  const [fromId, isTyping] = await typing
  check('Yazıyor göstergesi iletildi', fromId === patient.user.id && isTyping === true)

  // --- 4. WebRTC signaling handshake ---
  const ring = waitFor(dc, 'IncomingCall')
  await pc.invoke('CallUser', doctor.user.id, 'video')
  const [call] = await ring
  check('Görüntülü arama sinyali ulaştı', call.callType === 'video' && call.fromId === patient.user.id,
    `arayan: ${call.fromName}`)

  const accepted = waitFor(pc, 'CallAccepted')
  await dc.invoke('AcceptCall', patient.user.id)
  await accepted
  check('Arama kabul sinyali geri döndü', true)

  const offerGot = waitFor(dc, 'ReceiveOffer')
  await pc.invoke('SendOffer', doctor.user.id, { type: 'offer', sdp: 'v=0-test' })
  const [, offer] = await offerGot
  check('SDP offer iletildi', offer.sdp === 'v=0-test')

  const answerGot = waitFor(pc, 'ReceiveAnswer')
  await dc.invoke('SendAnswer', patient.user.id, { type: 'answer', sdp: 'v=0-answer' })
  const [, answer] = await answerGot
  check('SDP answer iletildi', answer.sdp === 'v=0-answer')

  const iceGot = waitFor(dc, 'ReceiveIceCandidate')
  await pc.invoke('SendIceCandidate', doctor.user.id, { candidate: 'candidate:test' })
  const [, ice] = await iceGot
  check('ICE candidate iletildi', ice.candidate === 'candidate:test')

  const ended = waitFor(dc, 'CallEnded')
  await pc.invoke('EndCall', doctor.user.id)
  await ended
  check('Arama sonlandırma sinyali iletildi', true)

  await pc.stop(); await dc.stop()

  const failed = results.filter((r) => !r.ok)
  console.log(`\n${results.length - failed.length}/${results.length} test geçti`)
  process.exit(failed.length ? 1 : 0)
}

run().catch((e) => { console.error('❌ HATA:', e.message); process.exit(1) })
