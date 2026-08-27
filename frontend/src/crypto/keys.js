// Uçtan uca şifreleme. Tasarım:
//
// - Her kullanıcının RSA-OAEP 2048 anahtar çifti var. Açık anahtar sunucuda
//   herkese açık durur; özel anahtar, kullanıcının parolasından PBKDF2 ile
//   türetilen bir AES anahtarıyla sarmalanmış olarak saklanır.
// - Ham parola sunucuya hiç gitmez. Kimlik doğrulama için parolanın ayrı bir
//   türevi (authVerifier) gönderilir. Sunucu ham parolayı görseydi aynı
//   sarmalama anahtarını üretip özel anahtarı açabilirdi.
// - Mesajlar rastgele bir AES-GCM anahtarıyla şifrelenir; o anahtar hem alıcının
//   hem gönderenin açık anahtarıyla ayrı ayrı şifrelenir, böylece gönderen de
//   kendi yazdığını okuyabilir.
//
// Sunucu tarafı karşılığı: backend/Services/AuthVerifier.cs

const enc = new TextEncoder()
const dec = new TextDecoder()

// Büyük tamponlarda String.fromCharCode(...spread) yığını taşırabildiği için
// parça parça ilerliyoruz.
const b64 = (buf) => {
  const bytes = new Uint8Array(buf)
  let out = ''
  for (let i = 0; i < bytes.length; i += 0x8000) {
    out += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000))
  }
  return btoa(out)
}

const unb64 = (s) => Uint8Array.from(atob(s), (c) => c.charCodeAt(0))

const AUTH_PREFIX = 'hellodoctor:auth:v1:'
const WRAP_PREFIX = 'hellodoctor:wrap:v1:'
const PBKDF2_ITERATIONS = 210_000

export const cryptoAvailable = () =>
  typeof crypto !== 'undefined' && !!crypto.subtle

// Sunucuya parola yerine gönderilen değer. backend/Services/AuthVerifier.cs
// ile birebir aynı diziyi üretmeli.
export async function authVerifier(password) {
  const digest = await crypto.subtle.digest('SHA-256', enc.encode(AUTH_PREFIX + password))
  return b64(digest)
}

async function wrapKeyFromPassword(password, salt) {
  const base = await crypto.subtle.importKey(
    'raw', enc.encode(WRAP_PREFIX + password), 'PBKDF2', false, ['deriveKey'])

  return crypto.subtle.deriveKey(
    { name: 'PBKDF2', salt, iterations: PBKDF2_ITERATIONS, hash: 'SHA-256' },
    base,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt', 'decrypt'],
  )
}

// Yeni anahtar çifti üretir ve özel anahtarı parolayla sarmalar.
// Döner: { bundle, privateKey } — bundle sunucuya gider, privateKey cihazda kalır.
export async function createKeyBundle(password) {
  const pair = await crypto.subtle.generateKey(
    {
      name: 'RSA-OAEP',
      modulusLength: 2048,
      publicExponent: new Uint8Array([1, 0, 1]),
      hash: 'SHA-256',
    },
    true,
    ['encrypt', 'decrypt'],
  )

  const publicKey = b64(await crypto.subtle.exportKey('spki', pair.publicKey))
  const pkcs8 = await crypto.subtle.exportKey('pkcs8', pair.privateKey)

  const salt = crypto.getRandomValues(new Uint8Array(16))
  const iv = crypto.getRandomValues(new Uint8Array(12))
  const wrapKey = await wrapKeyFromPassword(password, salt)
  const wrapped = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, wrapKey, pkcs8)

  // Özel anahtarı bir daha dışarı çıkarılamaz biçimde geri alıyoruz: cihazda
  // saklanan sürüm export edilemesin.
  const privateKey = await crypto.subtle.importKey(
    'pkcs8', pkcs8, { name: 'RSA-OAEP', hash: 'SHA-256' }, false, ['decrypt'])

  return {
    bundle: {
      publicKey,
      wrappedPrivateKey: b64(wrapped),
      keyWrapSalt: b64(salt),
      keyWrapIv: b64(iv),
    },
    privateKey,
  }
}

// Sunucudan gelen sarmalı parolayla açar. Parola yanlışsa AES-GCM doğrulaması
// başarısız olur ve hata fırlar.
export async function unwrapPrivateKey(password, bundle) {
  const wrapKey = await wrapKeyFromPassword(password, unb64(bundle.keyWrapSalt))
  const pkcs8 = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: unb64(bundle.keyWrapIv) },
    wrapKey,
    unb64(bundle.wrappedPrivateKey),
  )
  return crypto.subtle.importKey(
    'pkcs8', pkcs8, { name: 'RSA-OAEP', hash: 'SHA-256' }, false, ['decrypt'])
}

const importPublicKey = (spki) =>
  crypto.subtle.importKey('spki', unb64(spki), { name: 'RSA-OAEP', hash: 'SHA-256' }, false, ['encrypt'])

// Mesajı şifreler. Dönen nesne doğrudan sunucuya gönderilen alanlara karşılık gelir.
export async function encryptMessage(text, myPublicKey, peerPublicKey) {
  const aes = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt'])
  const iv = crypto.getRandomValues(new Uint8Array(12))
  const ciphertext = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, aes, enc.encode(text))

  const raw = await crypto.subtle.exportKey('raw', aes)
  const [mine, theirs] = await Promise.all([
    importPublicKey(myPublicKey),
    importPublicKey(peerPublicKey),
  ])
  const [forSender, forRecipient] = await Promise.all([
    crypto.subtle.encrypt({ name: 'RSA-OAEP' }, mine, raw),
    crypto.subtle.encrypt({ name: 'RSA-OAEP' }, theirs, raw),
  ])

  return {
    text: b64(ciphertext),
    encrypted: true,
    iv: b64(iv),
    keyForSender: b64(forSender),
    keyForRecipient: b64(forRecipient),
  }
}

// Çözülemezse null döner (anahtar yok, şifre sıfırlanmış, malzeme eksik).
// Çağıran taraf bunu kullanıcıya "okunamıyor" olarak gösterir.
export async function decryptMessage(msg, privateKey, isMine) {
  // Şifreleme öncesi yazılmış mesajlar düz metin olarak duruyor.
  if (!msg.encrypted) return msg.text
  if (!privateKey) return null

  const wrapped = isMine ? msg.keyForSender : msg.keyForRecipient
  if (!wrapped || !msg.iv) return null

  try {
    const raw = await crypto.subtle.decrypt({ name: 'RSA-OAEP' }, privateKey, unb64(wrapped))
    const aes = await crypto.subtle.importKey('raw', raw, { name: 'AES-GCM' }, false, ['decrypt'])
    const plain = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: unb64(msg.iv) }, aes, unb64(msg.text))
    return dec.decode(plain)
  } catch {
    return null
  }
}
