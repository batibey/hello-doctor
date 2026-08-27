// Birden fazla backend örneğiyle çalışmayı doğrular: istemciler AYRI
// örneklere bağlanır, mesajlaşma ve WebRTC sinyalleşmesi aralarında çalışmalı.
//
// Gereken: Redis + iki backend örneği.
//   docker compose up -d redis
//   ConnectionStrings__Redis=localhost:6379 dotnet run --urls http://localhost:5088
//   ConnectionStrings__Redis=localhost:6379 dotnet run --urls http://localhost:5090
//
// Çalıştır: node scaleout-test.mjs
import * as signalR from '@microsoft/signalr'
import { createHash } from 'node:crypto'

const A = process.env.HD_API_A || 'http://localhost:5088'
const B = process.env.HD_API_B || 'http://localhost:5090'

const authVerifier = (p) =>
  createHash('sha256').update(`hellodoctor:auth:v1:${p}`).digest('base64')

const login = async (api, email, role) => {
  const r = await fetch(`${api}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password: authVerifier('1234'), role }),
  })
  if (!r.ok) throw new Error(`login ${email} @ ${api}: ${r.status}`)
  return r.json()
}

const connect = async (api, token) => {
  const c = new signalR.HubConnectionBuilder()
    .withUrl(`${api}/hubs/call?access_token=${token}`, {
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets,
    })
    .configureLogging(signalR.LogLevel.Error)
    .build()
  await c.start()
  return c
}

const waitFor = (conn, event, timeout = 6000) =>
  new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error(`timeout waiting for ${event}`)), timeout)
    conn.on(event, (...args) => { clearTimeout(t); resolve(args) })
  })

const noEventWithin = (conn, event, ms = 700) =>
  new Promise((resolve) => {
    const handler = () => { conn.off(event, handler); resolve(false) }
    conn.on(event, handler)
    setTimeout(() => { conn.off(event, handler); resolve(true) }, ms)
  })

const results = []
const check = (name, ok, detail = '') => {
  results.push(ok)
  console.log(`${ok ? '✅' : '❌'} ${name}${detail ? ` — ${detail}` : ''}`)
}

const run = async () => {
  // Hasta A örneğine, doktor B örneğine bağlanıyor.
  const patient = await login(A, 'hasta@hellodoctor.com', 'Patient')
  const doctor = await login(B, 'dr.elif@hellodoctor.com', 'Doctor')
  const other = await login(B, 'zeynep@hellodoctor.com', 'Patient')

  const pc = await connect(A, patient.token)   // örnek A
  const dc = await connect(B, doctor.token)    // örnek B
  const oc = await connect(B, other.token)     // örnek B
  check('İstemciler ayrı örneklere bağlandı', true, 'hasta→A, doktor→B, zeynep→B')

  // --- Mesaj: A → B ---
  const text = `Scaleout ${Date.now()}`
  const incoming = waitFor(dc, 'ReceiveMessage')
  await pc.invoke('SendMessage', doctor.user.id, text, false, null, null, null)
  const [msg] = await incoming
  check('Mesaj örnekler arasında iletildi', msg.text === text, `A → B`)

  // --- Yazıyor göstergesi: A → B ---
  const typing = waitFor(dc, 'Typing')
  await pc.invoke('Typing', doctor.user.id, true)
  const [fromId, isTyping] = await typing
  check('Yazıyor göstergesi örnekler arasında iletildi',
    fromId === patient.user.id && isTyping === true)

  // --- Varlık: B'ye bağlı kullanıcı A'dan çevrimiçi görünmeli ---
  const ring = waitFor(dc, 'IncomingCall')
  const started = await pc.invoke('CallUser', doctor.user.id, 'video')
  await ring
  check('Diğer örnekteki kullanıcı çevrimiçi görüldü ve zil çaldı', started?.ok === true)

  // --- Meşgul: eşleşme durumu paylaşılıyor mu ---
  const busy = await oc.invoke('CallUser', doctor.user.id, 'voice')
  check('Görüşme eşleşmesi örnekler arasında görünüyor',
    busy?.ok === false && busy?.reason === 'busy', JSON.stringify(busy))

  // --- Sinyalleşme el sıkışması ---
  const accepted = waitFor(pc, 'CallAccepted')
  await dc.invoke('AcceptCall', patient.user.id)
  await accepted
  check('Kabul sinyali örnekler arasında döndü', true)

  const offerGot = waitFor(dc, 'ReceiveOffer')
  await pc.invoke('SendOffer', doctor.user.id, { type: 'offer', sdp: 'v=0-scaleout' })
  const [, offer] = await offerGot
  check('SDP offer örnekler arasında iletildi', offer.sdp === 'v=0-scaleout')

  // --- Yetkilendirme örnekler arasında da geçerli ---
  const noOffer = noEventWithin(pc, 'ReceiveOffer')
  await oc.invoke('SendOffer', patient.user.id, { type: 'offer', sdp: 'sahte' })
  check('Yabancı sinyali diğer örnekte de reddedildi', await noOffer)

  // --- Bitirme ---
  const ended = waitFor(dc, 'CallEnded')
  await pc.invoke('EndCall', doctor.user.id)
  await ended
  check('Bitirme sinyali örnekler arasında iletildi', true)

  // --- Eşleşme temizlendi, yeniden aranabilmeli ---
  const reRing = waitFor(dc, 'IncomingCall')
  const again = await oc.invoke('CallUser', doctor.user.id, 'voice')
  await reRing
  check('Görüşme bitince eşleşme paylaşılan depodan temizlendi', again?.ok === true)
  await oc.invoke('EndCall', doctor.user.id)

  // --- Çevrimdışı kullanıcı ---
  const away = await login(A, 'dr.canan@hellodoctor.com', 'Doctor')
  const offline = await pc.invoke('CallUser', away.user.id, 'voice')
  check('Hiçbir örneğe bağlı olmayan kullanıcı çevrimdışı bildirildi',
    offline?.ok === false && offline?.reason === 'offline')

  // --- Bağlantı kopması diğer örneğe ulaşıyor mu ---
  const ring2 = waitFor(oc, 'IncomingCall')
  await pc.invoke('CallUser', other.user.id, 'voice')
  await ring2
  const peerGone = waitFor(oc, 'CallEnded')
  await pc.stop()
  await peerGone
  check('Bir örnekteki kopma diğer örneğe bildirildi', true)

  await dc.stop(); await oc.stop()

  const failed = results.filter((r) => !r).length
  console.log(`\n${results.length - failed}/${results.length} test geçti`)
  process.exit(failed ? 1 : 0)
}

run().catch((e) => { console.error('❌ HATA:', e.message); process.exit(1) })
