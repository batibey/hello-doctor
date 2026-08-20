// WebRTC ICE yapılandırması. Değerler ortam değişkenlerinden gelir (.env.local),
// böylece TURN kimlik bilgileri kaynak koda gömülmez.

const parseList = (value) =>
  (value || '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)

// STUN yalnızca dış adresi bildirir, trafik taşımaz — public sunucular yeterlidir.
const DEFAULT_STUN = ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302']

const turnUrls = parseList(import.meta.env.VITE_TURN_URLS)

export const hasTurn = turnUrls.length > 0

export function buildIceServers() {
  const stun = parseList(import.meta.env.VITE_STUN_URLS)
  const servers = [{ urls: stun.length ? stun : DEFAULT_STUN }]

  if (hasTurn) {
    servers.push({
      urls: turnUrls,
      username: import.meta.env.VITE_TURN_USERNAME || undefined,
      credential: import.meta.env.VITE_TURN_CREDENTIAL || undefined,
    })
  }

  return servers
}

// 'relay' yapılırsa doğrudan bağlantı denenmez; TURN'ün gerçekten çalıştığını
// doğrulamak için kullanılır. Üretimde 'all' kalmalı.
export const iceTransportPolicy =
  import.meta.env.VITE_ICE_TRANSPORT_POLICY === 'relay' ? 'relay' : 'all'

export function buildRtcConfig() {
  return { iceServers: buildIceServers(), iceTransportPolicy }
}
