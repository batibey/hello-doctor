// Uçtan uca şifrelemenin doğrulaması. Tarayıcı gerekmiyor: WebCrypto Node 18+
// içinde global olarak var, uygulamanın kullandığı modülün aynısı çalıştırılıyor.
//
// Çalıştır: node crypto-test.mjs
import { createHash, webcrypto } from 'node:crypto'

// WebCrypto tarayıcıda global; Node 19'a kadar değil. Modülü olduğu gibi
// çalıştırabilmek için köprüleniyor.
if (!globalThis.crypto) globalThis.crypto = webcrypto

const { authVerifier, createKeyBundle, unwrapPrivateKey, encryptMessage, decryptMessage } =
  await import('./src/crypto/keys.js')

const results = []
const check = (name, ok, detail = '') => {
  results.push(ok)
  console.log(`${ok ? '✅' : '❌'} ${name}${detail ? ` — ${detail}` : ''}`)
}

const run = async () => {
  // --- 1. Sunucuyla aynı doğrulayıcı üretiliyor mu ---
  // backend/Services/AuthVerifier.cs ile birebir aynı olmalı, yoksa giriş kırılır.
  const expected = createHash('sha256').update('hellodoctor:auth:v1:1234').digest('base64')
  check('authVerifier sunucu türeviyle aynı', await authVerifier('1234') === expected)

  // Ham parola türevden geri elde edilemiyor olmalı.
  const verifier = await authVerifier('SifreTest123')
  check('doğrulayıcı ham parolayı içermiyor', !verifier.includes('SifreTest123'))

  // --- 2. Anahtar sarmalama ---
  const ayse = await createKeyBundle('AyseSifre123')
  const ali = await createKeyBundle('AliSifre456')
  check('anahtar çifti üretildi',
    ayse.bundle.publicKey.length > 300 && ayse.bundle.wrappedPrivateKey.length > 1000,
    `açık anahtar ${ayse.bundle.publicKey.length} karakter`)

  const unwrapped = await unwrapPrivateKey('AyseSifre123', ayse.bundle)
  check('doğru parolayla özel anahtar açıldı', !!unwrapped)

  let wrongFailed = false
  try {
    await unwrapPrivateKey('YanlisSifre', ayse.bundle)
  } catch {
    wrongFailed = true
  }
  check('yanlış parolayla açılamadı', wrongFailed)

  // Sarmalı tuz her seferinde farklı olmalı; aynı parola aynı çıktıyı vermemeli.
  const ayse2 = await createKeyBundle('AyseSifre123')
  check('her sarmalama farklı tuz kullanıyor',
    ayse.bundle.keyWrapSalt !== ayse2.bundle.keyWrapSalt)

  // --- 3. Mesaj şifreleme ---
  const text = 'Tansiyon değerleriniz 130/85, ilacı sabah alın.'
  const msg = await encryptMessage(text, ayse.bundle.publicKey, ali.bundle.publicKey)

  check('şifreli metin düz metni içermiyor',
    !msg.text.includes('Tansiyon') && !Buffer.from(msg.text, 'base64').toString('utf8').includes('Tansiyon'))
  check('her iki taraf için ayrı anahtar var',
    !!msg.keyForSender && !!msg.keyForRecipient && msg.keyForSender !== msg.keyForRecipient)

  // Sunucunun gördüğü kayıt: alıcı tarafında çözülüyor mu?
  const stored = { ...msg, senderId: 'ayse', recipientId: 'ali' }

  const asRecipient = await decryptMessage(stored, ali.privateKey, false)
  check('alıcı mesajı çözebildi', asRecipient === text, `"${asRecipient}"`)

  const asSender = await decryptMessage(stored, ayse.privateKey, true)
  check('gönderen kendi mesajını okuyabildi', asSender === text)

  // --- 4. Üçüncü taraf okuyamamalı ---
  const mehmet = await createKeyBundle('MehmetSifre789')
  const asStranger = await decryptMessage(stored, mehmet.privateKey, false)
  check('yabancı mesajı çözemedi', asStranger === null)

  // Yanlış taraftan çözme denemesi de başarısız olmalı.
  const wrongSide = await decryptMessage(stored, ali.privateKey, true)
  check('alıcı, gönderenin anahtarıyla çözemedi', wrongSide === null)

  // --- 5. Şifre sıfırlandığında eski mesajlar okunamaz ---
  const aliAfterReset = await createKeyBundle('AliYeniSifre999')
  const afterReset = await decryptMessage(stored, aliAfterReset.privateKey, false)
  check('sıfırlama sonrası eski mesaj okunamıyor', afterReset === null)

  // --- 6. Şifreleme öncesi düz metin mesajlar hâlâ görünüyor ---
  const legacy = { encrypted: false, text: 'Eski düz metin mesaj' }
  check('eski düz metin mesajlar bozulmadı',
    await decryptMessage(legacy, ali.privateKey, false) === 'Eski düz metin mesaj')

  const failed = results.filter((r) => !r).length
  console.log(`\n${results.length - failed}/${results.length} test geçti`)
  process.exit(failed ? 1 : 0)
}

run().catch((e) => { console.error('❌ HATA:', e); process.exit(1) })
