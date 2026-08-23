// Metered API anahtarını kullanarak TURN kimlik bilgilerini çeker ve
// .env.local dosyasına yazar.
//
// Metered'ın döndürdüğü kimlik bilgileri süreli olduğu için bu işin elle
// yapılması tekrar eden bir angarya; betik hem onu ortadan kaldırıyor hem de
// API anahtarının komut geçmişine veya kaynak koda düşmesini engelliyor.
//
// Kurulum:
//   1. Metered panelinde "Show API Key" ile anahtarı kopyalayın
//   2. frontend/.metered-key dosyasına yapıştırın (git'e girmez)
//   3. node fetch-turn.mjs

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const dir = path.dirname(fileURLToPath(import.meta.url))
const keyFile = path.join(dir, '.metered-key')
const envFile = path.join(dir, '.env.local')

const SUBDOMAIN = process.env.METERED_SUBDOMAIN || 'hidoctor'

if (!fs.existsSync(keyFile)) {
  console.error(`\n${keyFile} bulunamadı.\n`)
  console.error('Metered panelinde "Show API Key" ile anahtarı kopyalayıp bu dosyaya yapıştırın:')
  console.error(`  echo "ANAHTARINIZ" > ${path.relative(process.cwd(), keyFile)}\n`)
  process.exit(2)
}

const apiKey = fs.readFileSync(keyFile, 'utf8').trim()
if (!apiKey) {
  console.error('.metered-key dosyası boş.')
  process.exit(2)
}

const url = `https://${SUBDOMAIN}.metered.live/api/v1/turn/credentials?apiKey=${encodeURIComponent(apiKey)}`

let servers
try {
  const res = await fetch(url)
  if (res.status === 401) {
    console.error('\n401 — API anahtarı geçersiz. Panelden yeniden kopyalayın.\n')
    process.exit(1)
  }
  if (!res.ok) {
    console.error(`\n${res.status} — ${(await res.text()).slice(0, 200)}\n`)
    process.exit(1)
  }
  servers = await res.json()
} catch (err) {
  console.error(`\nİstek başarısız: ${err.message}\n`)
  process.exit(1)
}

if (!Array.isArray(servers) || !servers.length) {
  console.error('\nBeklenmeyen yanıt biçimi:', JSON.stringify(servers).slice(0, 200), '\n')
  process.exit(1)
}

// Yanıtta hem STUN hem TURN girdileri karışık gelir; ayrı ayrı toplanmalı
// çünkü uygulamamız ikisini farklı değişkenlerde tutuyor.
const turn = servers.filter((s) => /^turns?:/.test(s.urls || ''))
const stun = servers.filter((s) => /^stun:/.test(s.urls || ''))

if (!turn.length) {
  console.error('\nYanıtta TURN adresi yok. Panelde kimlik bilgisi tanımlı mı?\n')
  process.exit(1)
}

const { username, credential } = turn[0]

// Aynı kimlik bilgisi tüm adreslerde geçerli; farklıysa uyaralım ki
// sessizce yanlış eşleşme olmasın.
if (turn.some((s) => s.username !== username || s.credential !== credential)) {
  console.warn('Uyarı: adresler farklı kimlik bilgileri taşıyor, ilki kullanılıyor.')
}

const lines = [
  '# node fetch-turn.mjs tarafından üretildi — elle düzenlemeyin.',
  `# Üretim zamanı: ${new Date().toISOString()}`,
  '',
  `VITE_TURN_URLS=${turn.map((s) => s.urls).join(',')}`,
  `VITE_TURN_USERNAME=${username}`,
  `VITE_TURN_CREDENTIAL=${credential}`,
  '',
  `VITE_STUN_URLS=${stun.map((s) => s.urls).join(',')}`,
  '',
  '# Doğrudan bağlantıyı kapatıp TURN\'ü sınamak için: relay',
  'VITE_ICE_TRANSPORT_POLICY=',
  '',
].join('\n')

fs.writeFileSync(envFile, lines)

console.log(`\n✓ ${path.relative(process.cwd(), envFile)} yazıldı`)
console.log(`  TURN adresi : ${turn.length}`)
console.log(`  STUN adresi : ${stun.length}`)
console.log(`  kullanıcı   : ${username.slice(0, 4)}…\n`)
console.log('Sıradaki adım:  node turn-test.mjs')
console.log('Ardından Vite\'ı yeniden başlatın (env yalnızca açılışta okunur).\n')
