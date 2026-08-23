import { createContext, useContext, useEffect, useRef, useState, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuth } from './AuthContext'
import { buildRtcConfig, hasTurn } from '../config/ice'

const RealtimeContext = createContext(null)

// Arama süresi dolmadan bağlantı kurulamazsa kullanıcıya hata gösterilir.
const RING_TIMEOUT_MS = 45_000
const CONNECT_TIMEOUT_MS = 30_000

// Call phases: ringing-out | ringing-in | connecting | active | reconnecting | failed
export function RealtimeProvider({ children }) {
  const { user } = useAuth()
  const connRef = useRef(null)
  const [connected, setConnected] = useState(false)

  const messageHandlers = useRef(new Set())
  const typingHandlers = useRef(new Set())

  const [call, setCall] = useState(null)
  const callRef = useRef(null)
  useEffect(() => { callRef.current = call }, [call])

  const pcRef = useRef(null)
  const localStreamRef = useRef(null)
  const [localStream, setLocalStream] = useState(null)
  const [remoteStream, setRemoteStream] = useState(null)
  const [muted, setMuted] = useState(false)
  const [camOff, setCamOff] = useState(false)
  const pendingCandidates = useRef([])
  const timeoutRef = useRef(null)

  const invoke = useCallback((method, ...args) => {
    if (connRef.current?.state === signalR.HubConnectionState.Connected)
      return connRef.current.invoke(method, ...args)
    return Promise.resolve()
  }, [])

  // ---------- Teardown ----------
  const cleanupMedia = useCallback(() => {
    pcRef.current?.close()
    pcRef.current = null
    localStreamRef.current?.getTracks().forEach((t) => t.stop())
    localStreamRef.current = null
    setLocalStream(null)
    setRemoteStream(null)
    pendingCandidates.current = []
    setMuted(false)
    setCamOff(false)
  }, [])

  // Aramayı hata durumuna düşürür. Kullanıcı kapatana kadar ekranda kalır.
  const failCall = useCallback((message, { notifyPeer = true } = {}) => {
    const cur = callRef.current
    if (cur?.peerId && notifyPeer) invoke('EndCall', cur.peerId)
    cleanupMedia()
    setCall({
      phase: 'failed',
      error: message,
      peerId: cur?.peerId,
      peerName: cur?.peerName || '',
      peerColor: cur?.peerColor || '#6366F1',
      callType: cur?.callType || 'voice',
    })
  }, [invoke, cleanupMedia])

  const dismissCall = useCallback(() => {
    cleanupMedia()
    setCall(null)
  }, [cleanupMedia])

  // WebRTC kurulum hataları tarayıcıdan tarayıcıya değişiyor ve telefonda
  // konsola bakmak mümkün olmadığı için sebebi ekrana taşıyoruz.
  const describeError = (err) => {
    const name = err?.name || 'Error'
    const msg = (err?.message || '').slice(0, 120)
    return msg ? `${name}: ${msg}` : name
  }

  // ---------- Media ----------
  // WhatsApp, Instagram gibi uygulamaların içinde açılan sayfalar iOS'ta
  // WKWebView'de çalışır ve kameraya hiç erişemez; kullanıcıya izin bile
  // sorulmaz, doğrudan NotAllowedError döner. Bu durumda "izinlerden açın"
  // demek yanıltıcı olur — yapılacak tek şey sayfayı Safari'de açmak.
  const isIosInAppBrowser = () => {
    const ua = navigator.userAgent
    if (!/iPhone|iPad|iPod/.test(ua)) return false
    // Gerçek Safari "Safari/", diğer iOS tarayıcıları kendi ekini taşır.
    return !/Safari\//.test(ua) && !/CriOS|FxiOS|EdgiOS|OPiOS/.test(ua)
  }

  const mediaErrorMessage = (err, callType) => {
    // Tarayıcılar getUserMedia'yı yalnızca güvenli bağlamda (HTTPS veya localhost) sunar.
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia)
      return 'Kamera ve mikrofon yalnızca HTTPS üzerinden veya localhost’ta kullanılabilir. Telefondan bağlanıyorsanız HTTPS gerekir.'

    switch (err?.name) {
      case 'NotAllowedError':
      case 'SecurityError':
        if (isIosInAppBrowser())
          return 'Bu sayfa bir uygulamanın içinde açıldığı için kameraya erişemiyor. Paylaş simgesine dokunup “Safari’de Aç” deyin.'
        // iOS'ta izin üç ayrı yerden kapatılmış olabilir ve Safari hiçbirinde
        // yeniden sormaz; kullanıcıyı doğru menüye yönlendirmezsek çıkmazda kalır.
        if (/iPhone|iPad|iPod/.test(navigator.userAgent))
          return 'Kamera/mikrofon izni kapalı. Adres çubuğundaki “ᴀA” simgesine dokunup Web Sitesi Ayarları’ndan izin verin. Orada görünmüyorsa Ayarlar › Safari › Kamera ve Mikrofon’u “Sor” yapın.'
        return 'Kamera/mikrofon izni reddedildi. Tarayıcı ayarlarından bu siteye izin verip sayfayı yenileyin.'
      case 'NotFoundError':
      case 'OverconstrainedError':
        return callType === 'video' ? 'Kamera bulunamadı.' : 'Mikrofon bulunamadı.'
      case 'NotReadableError':
        return 'Kamera veya mikrofon başka bir uygulama tarafından kullanılıyor.'
      default:
        return 'Medya cihazlarına erişilemedi.'
    }
  }

  const getMedia = useCallback(async (callType) => {
    if (!navigator.mediaDevices?.getUserMedia) {
      const err = new Error('mediaDevices unavailable')
      err.name = 'SecurityError'
      throw err
    }
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: true,
      video: callType === 'video',
    })
    localStreamRef.current = stream
    setLocalStream(stream)
    return stream
  }, [])

  // ---------- Peer connection ----------
  const createPeer = useCallback((peerId) => {
    const pc = new RTCPeerConnection(buildRtcConfig())

    pc.onicecandidate = (e) => {
      if (e.candidate) invoke('SendIceCandidate', peerId, e.candidate)
    }
    pc.ontrack = (e) => setRemoteStream(e.streams[0])

    pc.onconnectionstatechange = () => {
      switch (pc.connectionState) {
        case 'connected':
          setCall((c) => (c && c.phase !== 'failed' ? { ...c, phase: 'active' } : c))
          break
        case 'disconnected':
          // Geçici kopma — WebRTC kendi kendine toparlayabilir.
          setCall((c) => (c && c.phase === 'active' ? { ...c, phase: 'reconnecting' } : c))
          break
        case 'failed':
          failCall(
            hasTurn
              ? 'Bağlantı kurulamadı. Ağ bağlantınızı kontrol edip tekrar deneyin.'
              : 'Bağlantı kurulamadı. Ağınız doğrudan bağlantıya izin vermiyor olabilir; bu durumda TURN sunucusu gerekir.',
          )
          break
        default:
          break
      }
    }

    pcRef.current = pc
    return pc
  }, [invoke, failCall])

  // ---------- Call actions ----------
  const startCall = useCallback(async (peer, callType) => {
    if (callRef.current) return
    setCall({
      phase: 'ringing-out', peerId: peer.id, peerName: peer.fullName,
      peerColor: peer.avatarColor, callType, isCaller: true,
    })
    try {
      await getMedia(callType)
      await invoke('CallUser', peer.id, callType)
    } catch (err) {
      console.error('startCall failed', err)
      failCall(mediaErrorMessage(err, callType), { notifyPeer: false })
    }
  }, [getMedia, invoke, failCall])

  const acceptCall = useCallback(async () => {
    const cur = callRef.current
    if (!cur) return
    setCall((c) => c && { ...c, phase: 'connecting' })
    try {
      await getMedia(cur.callType)
      await invoke('AcceptCall', cur.peerId)
    } catch (err) {
      console.error('acceptCall failed', err)
      await invoke('RejectCall', cur.peerId)
      failCall(mediaErrorMessage(err, cur.callType), { notifyPeer: false })
    }
  }, [getMedia, invoke, failCall])

  const rejectCall = useCallback(async () => {
    const cur = callRef.current
    if (cur?.peerId) await invoke('RejectCall', cur.peerId)
    cleanupMedia()
    setCall(null)
  }, [invoke, cleanupMedia])

  const endCall = useCallback(async () => {
    const cur = callRef.current
    if (cur?.peerId) await invoke('EndCall', cur.peerId)
    cleanupMedia()
    setCall(null)
  }, [invoke, cleanupMedia])

  const toggleMute = useCallback(() => {
    const track = localStreamRef.current?.getAudioTracks()[0]
    if (track) { track.enabled = !track.enabled; setMuted(!track.enabled) }
  }, [])

  const toggleCam = useCallback(() => {
    const track = localStreamRef.current?.getVideoTracks()[0]
    if (track) { track.enabled = !track.enabled; setCamOff(!track.enabled) }
  }, [])

  // ---------- Timeouts ----------
  // Karşı taraf açmazsa ya da ICE takılırsa sessizce beklemek yerine hata göster.
  useEffect(() => {
    clearTimeout(timeoutRef.current)
    if (!call) return

    if (call.phase === 'ringing-out') {
      timeoutRef.current = setTimeout(
        () => failCall('Yanıt verilmedi.'), RING_TIMEOUT_MS)
    } else if (call.phase === 'connecting' || call.phase === 'reconnecting') {
      timeoutRef.current = setTimeout(
        () => failCall('Bağlantı zaman aşımına uğradı.'), CONNECT_TIMEOUT_MS)
    }

    return () => clearTimeout(timeoutRef.current)
  }, [call, failCall])

  // ---------- Chat API ----------
  const sendMessage = useCallback((recipientId, text) => invoke('SendMessage', recipientId, text), [invoke])
  const sendTyping = useCallback((recipientId, isTyping) => invoke('Typing', recipientId, isTyping), [invoke])
  const onMessage = useCallback((fn) => { messageHandlers.current.add(fn); return () => messageHandlers.current.delete(fn) }, [])
  const onTyping = useCallback((fn) => { typingHandlers.current.add(fn); return () => typingHandlers.current.delete(fn) }, [])

  // ---------- Connection lifecycle ----------
  useEffect(() => {
    if (!user) return
    const token = localStorage.getItem('hd_token')
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/call?access_token=${token}`)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()
    connRef.current = conn

    conn.on('ReceiveMessage', (m) => messageHandlers.current.forEach((fn) => fn(m)))
    conn.on('MessageSent', (m) => messageHandlers.current.forEach((fn) => fn(m)))
    conn.on('Typing', (fromId, isTyping) => typingHandlers.current.forEach((fn) => fn(fromId, isTyping)))

    conn.on('IncomingCall', (info) => {
      if (callRef.current) return // meşgul
      setCall({
        phase: 'ringing-in', peerId: info.fromId, peerName: info.fromName,
        peerColor: info.fromColor, callType: info.callType, isCaller: false,
      })
    })

    conn.on('CallAccepted', async (fromId) => {
      const cur = callRef.current
      if (!cur || !cur.isCaller) return
      setCall((c) => c && { ...c, phase: 'connecting' })
      try {
        const pc = createPeer(fromId)
        localStreamRef.current?.getTracks().forEach((t) => pc.addTrack(t, localStreamRef.current))
        const offer = await pc.createOffer()
        await pc.setLocalDescription(offer)
        await invoke('SendOffer', fromId, offer)
      } catch (err) {
        console.error('offer failed', err)
        failCall(`Görüşme başlatılamadı. (${describeError(err)})`)
      }
    })

    conn.on('ReceiveOffer', async (fromId, offer) => {
      try {
        const pc = pcRef.current || createPeer(fromId)
        localStreamRef.current?.getTracks().forEach((t) => pc.addTrack(t, localStreamRef.current))
        await pc.setRemoteDescription(new RTCSessionDescription(offer))
        for (const c of pendingCandidates.current) await pc.addIceCandidate(new RTCIceCandidate(c))
        pendingCandidates.current = []
        const answer = await pc.createAnswer()
        await pc.setLocalDescription(answer)
        await invoke('SendAnswer', fromId, answer)
      } catch (err) {
        console.error('answer failed', err)
        failCall(`Görüşme başlatılamadı. (${describeError(err)})`)
      }
    })

    conn.on('ReceiveAnswer', async (fromId, answer) => {
      const pc = pcRef.current
      if (!pc) return
      try {
        await pc.setRemoteDescription(new RTCSessionDescription(answer))
        for (const c of pendingCandidates.current) await pc.addIceCandidate(new RTCIceCandidate(c))
        pendingCandidates.current = []
      } catch (err) {
        console.error('setRemoteDescription failed', err)
        failCall(`Görüşme başlatılamadı. (${describeError(err)})`)
      }
    })

    conn.on('ReceiveIceCandidate', async (fromId, candidate) => {
      const pc = pcRef.current
      try {
        if (pc && pc.remoteDescription) await pc.addIceCandidate(new RTCIceCandidate(candidate))
        else pendingCandidates.current.push(candidate)
      } catch (err) {
        console.error('addIceCandidate failed', err)
      }
    })

    conn.on('CallRejected', () => {
      failCall('Arama reddedildi.', { notifyPeer: false })
    })
    conn.on('CallEnded', () => {
      cleanupMedia()
      setCall(null)
    })

    conn.start()
      .then(() => setConnected(true))
      .catch((e) => console.error('SignalR connect failed', e))

    conn.onreconnected(() => setConnected(true))
    conn.onclose(() => setConnected(false))

    return () => { conn.stop(); connRef.current = null; setConnected(false) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user])

  return (
    <RealtimeContext.Provider value={{
      connected, sendMessage, sendTyping, onMessage, onTyping,
      call, localStream, remoteStream, muted, camOff, hasTurn,
      startCall, acceptCall, rejectCall, endCall, dismissCall, toggleMute, toggleCam,
    }}>
      {children}
    </RealtimeContext.Provider>
  )
}

export const useRealtime = () => useContext(RealtimeContext)
