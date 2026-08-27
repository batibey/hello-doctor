// Özel anahtarın cihazda kalıcı deposu.
//
// localStorage kullanılamaz: yalnızca dize saklar, anahtarı oraya yazmak için
// dışa çıkarmak gerekirdi. IndexedDB CryptoKey nesnesini olduğu gibi saklar ve
// anahtar "extractable: false" kaldığı için JavaScript onu bir daha okuyamaz —
// sayfada XSS olsa bile anahtar dışarı taşınamaz, yalnızca kullanılabilir.
//
// Kullanıcı başına ayrı kayıt: aynı tarayıcıda iki hesapla giriş yapıldığında
// (hasta + doktor testi) anahtarlar birbirine karışmasın.

const DB_NAME = 'hellodoctor-keys'
const STORE = 'privateKeys'
const VERSION = 1

const open = () =>
  new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, VERSION)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE)) req.result.createObjectStore(STORE)
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })

const tx = async (mode, fn) => {
  const db = await open()
  try {
    return await new Promise((resolve, reject) => {
      const store = db.transaction(STORE, mode).objectStore(STORE)
      const req = fn(store)
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
  } finally {
    db.close()
  }
}

export const savePrivateKey = (userId, key) =>
  tx('readwrite', (s) => s.put(key, userId)).then(() => true, () => false)

export const loadPrivateKey = (userId) =>
  tx('readonly', (s) => s.get(userId)).then((k) => k || null, () => null)

export const clearPrivateKey = (userId) =>
  tx('readwrite', (s) => s.delete(userId)).then(() => true, () => false)
