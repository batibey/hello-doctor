// TURN kimlik bilgilerini gerçekten doğrular: sunucuya bir Allocate isteği
// gönderip relay adresi alınabiliyor mu diye bakar (RFC 5766).
//
// Tarayıcıda "arama kurulamadı" hatası TURN'den de, kameradan da, ağdan da
// kaynaklanabilir. Bu araç yalnızca TURN katmanını sınar, böylece hata
// ayıklarken hangi katmanın suçlu olduğu belirsiz kalmaz.
//
//   node turn-test.mjs                          # .env.local dosyasını okur
//   node turn-test.mjs turn:host:3478 kul sifre # doğrudan parametre

import dgram from 'node:dgram'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const MAGIC = 0x2112a442
const ALLOCATE = 0x0003
const SUCCESS = 0x0103
const ERROR = 0x0113

const ATTR = {
  USERNAME: 0x0006,
  MESSAGE_INTEGRITY: 0x0008,
  ERROR_CODE: 0x0009,
  REALM: 0x0014,
  NONCE: 0x0015,
  XOR_RELAYED_ADDRESS: 0x0016,
  REQUESTED_TRANSPORT: 0x0019,
}

const pad4 = (n) => (n + 3) & ~3

function encodeAttrs(attrs) {
  const parts = []
  for (const [type, value] of attrs) {
    const header = Buffer.alloc(4)
    header.writeUInt16BE(type, 0)
    header.writeUInt16BE(value.length, 2)
    parts.push(header, value, Buffer.alloc(pad4(value.length) - value.length))
  }
  return Buffer.concat(parts)
}

// MESSAGE-INTEGRITY, kendisi de dahil edilmiş uzunluk üzerinden hesaplanır;
// bu yüzden başlık iki kez yazılıyor.
function buildMessage(method, txId, attrs, key) {
  const body = encodeAttrs(attrs)
  const length = body.length + (key ? 24 : 0)

  const header = Buffer.alloc(20)
  header.writeUInt16BE(method, 0)
  header.writeUInt16BE(length, 2)
  header.writeUInt32BE(MAGIC, 4)
  txId.copy(header, 8)

  if (!key) return Buffer.concat([header, body])

  const hmac = crypto.createHmac('sha1', key).update(Buffer.concat([header, body])).digest()
  return Buffer.concat([header, body, encodeAttrs([[ATTR.MESSAGE_INTEGRITY, hmac]])])
}

function parse(msg) {
  const type = msg.readUInt16BE(0)
  const attrs = {}
  let off = 20
  const end = 20 + msg.readUInt16BE(2)

  while (off + 4 <= end && off + 4 <= msg.length) {
    const t = msg.readUInt16BE(off)
    const len = msg.readUInt16BE(off + 2)
    attrs[t] = msg.subarray(off + 4, off + 4 + len)
    off += 4 + pad4(len)
  }
  return { type, attrs }
}

function xorAddress(buf) {
  const port = buf.readUInt16BE(2) ^ (MAGIC >>> 16)
  const raw = buf.subarray(4)
  if (raw.length !== 4) return `[IPv6]:${port}` // relay IPv6 ise ayrıntı gerekmiyor
  const cookie = Buffer.alloc(4)
  cookie.writeUInt32BE(MAGIC, 0)
  const ip = Array.from(raw, (b, i) => b ^ cookie[i]).join('.')
  return `${ip}:${port}`
}

function errorText(attrs) {
  const buf = attrs[ATTR.ERROR_CODE]
  if (!buf) return 'bilinmeyen hata'
  const code = buf[2] * 100 + buf[3]
  return `${code} ${buf.subarray(4).toString('utf8')}`
}

function request(sock, host, port, msg, timeoutMs = 5000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      sock.removeListener('message', onMessage)
      reject(new Error(`sunucu ${timeoutMs} ms içinde yanıt vermedi`))
    }, timeoutMs)

    const onMessage = (buf) => {
      clearTimeout(timer)
      sock.removeListener('message', onMessage)
      resolve(parse(buf))
    }

    sock.on('message', onMessage)
    sock.send(msg, port, host, (err) => {
      if (err) {
        clearTimeout(timer)
        sock.removeListener('message', onMessage)
        reject(err)
      }
    })
  })
}

async function allocate(host, port, username, password) {
  const sock = dgram.createSocket('udp4')
  try {
    const txId = crypto.randomBytes(12)
    const transport = Buffer.alloc(4)
    transport[0] = 17 // UDP

    // İlk istek kimlik bilgisiz gider; sunucu realm ve nonce ile 401 döner.
    const probe = await request(sock, host, port,
      buildMessage(ALLOCATE, txId, [[ATTR.REQUESTED_TRANSPORT, transport]]))

    if (probe.type === SUCCESS) {
      return { ok: true, note: 'sunucu kimlik doğrulaması istemedi (açık relay)' }
    }

    const realm = probe.attrs[ATTR.REALM]
    const nonce = probe.attrs[ATTR.NONCE]
    if (!realm || !nonce) {
      return { ok: false, reason: `beklenmeyen yanıt: ${errorText(probe.attrs)}` }
    }

    const key = crypto.createHash('md5')
      .update(`${username}:${realm.toString('utf8')}:${password}`)
      .digest()

    const authed = await request(sock, host, port,
      buildMessage(ALLOCATE, crypto.randomBytes(12), [
        [ATTR.REQUESTED_TRANSPORT, transport],
        [ATTR.USERNAME, Buffer.from(username, 'utf8')],
        [ATTR.REALM, realm],
        [ATTR.NONCE, nonce],
      ], key))

    if (authed.type === ERROR) {
      return { ok: false, reason: errorText(authed.attrs), realm: realm.toString('utf8') }
    }

    const relayed = authed.attrs[ATTR.XOR_RELAYED_ADDRESS]
    if (!relayed) return { ok: false, reason: 'başarılı yanıtta relay adresi yok' }

    return { ok: true, relay: xorAddress(relayed), realm: realm.toString('utf8') }
  } finally {
    sock.close()
  }
}

function readEnvLocal() {
  const dir = path.dirname(fileURLToPath(import.meta.url))
  const file = path.join(dir, '.env.local')
  if (!fs.existsSync(file)) return {}

  return Object.fromEntries(
    fs.readFileSync(file, 'utf8')
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line && !line.startsWith('#'))
      .map((line) => {
        const i = line.indexOf('=')
        return [line.slice(0, i).trim(), line.slice(i + 1).trim()]
      })
      .filter(([k]) => k),
  )
}

// Kimlik bilgilerinin asıl kaynağı artık backend: GET /api/ice. Tarayıcının
// gerçekte kullanacağı değerleri sınamak için onları buradan almak gerekir.
async function fetchFromApi() {
  const api = process.env.HD_API || 'http://localhost:5088'
  const verifier = crypto.createHash('sha256')
    .update('hellodoctor:auth:v1:1234').digest('base64')

  const login = await fetch(`${api}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'hasta@hellodoctor.com', password: verifier, role: 'Patient' }),
  })
  if (!login.ok) throw new Error(`giriş başarısız: ${login.status}`)
  const { token } = await login.json()

  const res = await fetch(`${api}/api/ice`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(`/api/ice ${res.status}`)
  const config = await res.json()

  const turn = config.iceServers.find((s) =>
    (Array.isArray(s.urls) ? s.urls : [s.urls]).some((u) => u.startsWith('turn')))
  if (!turn) return null

  return {
    urls: (Array.isArray(turn.urls) ? turn.urls : [turn.urls]).join(','),
    username: turn.username || '',
    credential: turn.credential || '',
  }
}

// turn:host:port?transport=tcp → { host, port, udp }
function parseUrl(url) {
  const m = /^turns?:([^:?]+)(?::(\d+))?(?:\?(.*))?$/.exec(url.trim())
  if (!m) return null
  return {
    host: m[1],
    port: Number(m[2] || 3478),
    udp: !/transport=tcp/i.test(m[3] || ''),
    secure: url.startsWith('turns:'),
  }
}

const [argUrl, argUser, argPass] = process.argv.slice(2)

// Öncelik: komut satırı → backend (/api/ice) → eski .env.local
let source = 'komut satırı'
let raw = argUrl ? { urls: argUrl, username: argUser || '', credential: argPass || '' } : null

if (!raw) {
  try {
    raw = await fetchFromApi()
    source = 'backend /api/ice'
  } catch (err) {
    console.error(`/api/ice okunamadı (${err.message}), .env.local'e düşülüyor\n`)
  }
}

if (!raw) {
  const env = readEnvLocal()
  if (env.VITE_TURN_URLS) {
    raw = { urls: env.VITE_TURN_URLS, username: env.VITE_TURN_USERNAME || '', credential: env.VITE_TURN_CREDENTIAL || '' }
    source = '.env.local (eski yöntem)'
  }
}

const urls = (raw?.urls || '').split(',').map((s) => s.trim()).filter(Boolean)
const username = raw?.username || ''
const password = raw?.credential || ''

if (!urls.length) {
  console.error('TURN adresi bulunamadı.')
  console.error('Backend çalışıyor ve Ice__MeteredApiKey tanımlı mı? Ya da:')
  console.error('  node turn-test.mjs turn:host:3478 kullanici sifre')
  process.exit(2)
}

console.log(`kaynak: ${source}`)

console.log(`\nTURN doğrulaması — kullanıcı: ${username || '(yok)'}\n`)

let passed = 0
let skipped = 0

for (const url of urls) {
  const parsed = parseUrl(url)
  process.stdout.write(`  ${url}\n`)

  if (!parsed) {
    console.log('    ✗ adres ayrıştırılamadı\n')
    continue
  }

  // Bu araç yalnızca UDP konuşur; TCP/TLS uçları tarayıcıda çalışsa da
  // burada sınanamaz, o yüzden başarısız saymak yerine atlanıyor.
  if (!parsed.udp || parsed.secure) {
    console.log('    ⊘ atlandı (yalnızca UDP sınanıyor)\n')
    skipped++
    continue
  }

  try {
    const result = await allocate(parsed.host, parsed.port, username, password)
    if (result.ok) {
      passed++
      console.log(`    ✓ relay ayrıldı → ${result.relay || result.note}`)
      if (result.realm) console.log(`      realm: ${result.realm}`)
    } else {
      console.log(`    ✗ ${result.reason}`)
      if (/^401|^441/.test(result.reason)) console.log('      → kullanıcı adı veya şifre hatalı')
      if (/^486|^508/.test(result.reason)) console.log('      → kota dolmuş olabilir')
    }
  } catch (err) {
    console.log(`    ✗ ${err.message}`)
    console.log('      → adres yanlış olabilir veya ağınız bu portu engelliyor')
  }
  console.log()
}

const total = urls.length - skipped
console.log(total > 0 && passed === total
  ? `Tüm UDP uçları çalışıyor (${passed}/${total}). TURN kullanıma hazır.`
  : `${passed}/${total} uç çalışıyor.${skipped ? ` ${skipped} uç atlandı.` : ''}`)
console.log()

process.exit(total > 0 && passed === total ? 0 : 1)
