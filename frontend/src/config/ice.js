// WebRTC ICE yapılandırması sunucudan çalışma anında gelir (GET /api/ice).
//
// Eskiden VITE_TURN_* değişkenlerinden okunuyordu; Vite bunları derleme
// sırasında pakete gömdüğü için iki sorun vardı: kimlik bilgisinin süresi
// dolduğunda dağıtılmış paket sessizce bozuluyor (ve kullanıcıya "ağınızı
// kontrol edin" gibi yanlış bir sebep gösteriliyor), ayrıca uygulamayı açan
// herkes kimlik bilgisini okuyup kotayı harcayabiliyordu.

import api from '../api/client'

// Sunucuya ulaşılamazsa görüşme hiç kurulamasın istemiyoruz: STUN'la doğrudan
// bağlantı çoğu ağda çalışır. TURN gerektiren durumlarda hata mesajı bunu söyler.
const FALLBACK = {
  rtc: {
    iceServers: [{ urls: ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302'] }],
    iceTransportPolicy: 'all',
  },
  hasTurn: false,
}

let cache = null

const toRtcConfig = (data) => ({
  rtc: {
    iceServers: data.iceServers.map((s) => ({
      urls: s.urls,
      // undefined bırakılmalı: null username RTCPeerConnection'ı hataya sokar.
      username: s.username || undefined,
      credential: s.credential || undefined,
    })),
    iceTransportPolicy: data.iceTransportPolicy === 'relay' ? 'relay' : 'all',
  },
  hasTurn: !!data.hasTurn,
  expiresAt: Date.parse(data.expiresAt) || 0,
})

// Süresi dolmak üzereyse tazeler. Sunucu erişilemezse elde ne varsa onunla
// devam eder — arama, kimlik tazelenemedi diye hiç başlamamış olmaz.
export async function getIceConfig() {
  const fresh = cache && Date.now() < cache.expiresAt - 60_000
  if (fresh) return cache

  try {
    const { data } = await api.get('/ice')
    cache = toRtcConfig(data)
  } catch {
    if (!cache) return FALLBACK
  }
  return cache
}

export const clearIceCache = () => { cache = null }
