import { createContext, useContext, useEffect, useRef, useState, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuth } from './AuthContext'

const RealtimeContext = createContext(null)

const ICE = { iceServers: [{ urls: ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302'] }] }

// Call phases: idle | ringing-out | ringing-in | connecting | active
export function RealtimeProvider({ children }) {
  const { user } = useAuth()
  const connRef = useRef(null)
  const [connected, setConnected] = useState(false)

  // chat listeners registered by screens (keyed callback set)
  const messageHandlers = useRef(new Set())
  const typingHandlers = useRef(new Set())

  // ---- Call state ----
  const [call, setCall] = useState(null) // { phase, peerId, peerName, peerColor, callType, isCaller }
  const pcRef = useRef(null)
  const localStreamRef = useRef(null)
  const [localStream, setLocalStream] = useState(null)
  const [remoteStream, setRemoteStream] = useState(null)
  const [muted, setMuted] = useState(false)
  const [camOff, setCamOff] = useState(false)
  const pendingCandidates = useRef([])

  const invoke = useCallback((method, ...args) => {
    if (connRef.current?.state === signalR.HubConnectionState.Connected)
      return connRef.current.invoke(method, ...args)
    return Promise.resolve()
  }, [])

  // ---------- WebRTC helpers ----------
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

  const createPeer = useCallback((peerId) => {
    const pc = new RTCPeerConnection(ICE)
    pc.onicecandidate = (e) => {
      if (e.candidate) invoke('SendIceCandidate', peerId, e.candidate)
    }
    pc.ontrack = (e) => setRemoteStream(e.streams[0])
    pcRef.current = pc
    return pc
  }, [invoke])

  const getMedia = useCallback(async (callType) => {
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: true,
      video: callType === 'video',
    })
    localStreamRef.current = stream
    setLocalStream(stream)
    return stream
  }, [])

  // ---------- Public call actions ----------
  const startCall = useCallback(async (peer, callType) => {
    try {
      setCall({ phase: 'ringing-out', peerId: peer.id, peerName: peer.fullName, peerColor: peer.avatarColor, callType, isCaller: true })
      await getMedia(callType)
      await invoke('CallUser', peer.id, callType)
    } catch (err) {
      console.error('startCall failed', err)
      cleanupMedia()
      setCall(null)
      alert('Kamera/mikrofon erişimi reddedildi veya kullanılamıyor.')
    }
  }, [getMedia, invoke, cleanupMedia])

  const acceptCall = useCallback(async () => {
    setCall((c) => c && { ...c, phase: 'connecting' })
    const cur = callRef.current
    try {
      await getMedia(cur.callType)
      await invoke('AcceptCall', cur.peerId)
    } catch (err) {
      console.error('acceptCall failed', err)
      await invoke('RejectCall', cur.peerId)
      cleanupMedia(); setCall(null)
    }
  }, [getMedia, invoke, cleanupMedia])

  const rejectCall = useCallback(async () => {
    const cur = callRef.current
    if (cur) await invoke('RejectCall', cur.peerId)
    cleanupMedia(); setCall(null)
  }, [invoke, cleanupMedia])

  const endCall = useCallback(async () => {
    const cur = callRef.current
    if (cur) await invoke('EndCall', cur.peerId)
    cleanupMedia(); setCall(null)
  }, [invoke, cleanupMedia])

  const toggleMute = useCallback(() => {
    const track = localStreamRef.current?.getAudioTracks()[0]
    if (track) { track.enabled = !track.enabled; setMuted(!track.enabled) }
  }, [])

  const toggleCam = useCallback(() => {
    const track = localStreamRef.current?.getVideoTracks()[0]
    if (track) { track.enabled = !track.enabled; setCamOff(!track.enabled) }
  }, [])

  // keep a ref to current call for use inside socket handlers
  const callRef = useRef(call)
  useEffect(() => { callRef.current = call }, [call])

  // ---------- Chat API for screens ----------
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

    // Incoming call
    conn.on('IncomingCall', (info) => {
      if (callRef.current) { return } // busy — ignore
      setCall({ phase: 'ringing-in', peerId: info.fromId, peerName: info.fromName, peerColor: info.fromColor, callType: info.callType, isCaller: false })
    })

    // Callee accepted -> caller creates offer
    conn.on('CallAccepted', async (fromId) => {
      const cur = callRef.current
      if (!cur || !cur.isCaller) return
      setCall((c) => c && { ...c, phase: 'connecting' })
      const pc = createPeer(fromId)
      localStreamRef.current?.getTracks().forEach((t) => pc.addTrack(t, localStreamRef.current))
      const offer = await pc.createOffer()
      await pc.setLocalDescription(offer)
      await invoke('SendOffer', fromId, offer)
    })

    conn.on('ReceiveOffer', async (fromId, offer) => {
      const pc = pcRef.current || createPeer(fromId)
      localStreamRef.current?.getTracks().forEach((t) => pc.addTrack(t, localStreamRef.current))
      await pc.setRemoteDescription(new RTCSessionDescription(offer))
      for (const c of pendingCandidates.current) await pc.addIceCandidate(new RTCIceCandidate(c))
      pendingCandidates.current = []
      const answer = await pc.createAnswer()
      await pc.setLocalDescription(answer)
      await invoke('SendAnswer', fromId, answer)
      setCall((c) => c && { ...c, phase: 'active' })
    })

    conn.on('ReceiveAnswer', async (fromId, answer) => {
      const pc = pcRef.current
      if (!pc) return
      await pc.setRemoteDescription(new RTCSessionDescription(answer))
      for (const c of pendingCandidates.current) await pc.addIceCandidate(new RTCIceCandidate(c))
      pendingCandidates.current = []
      setCall((c) => c && { ...c, phase: 'active' })
    })

    conn.on('ReceiveIceCandidate', async (fromId, candidate) => {
      const pc = pcRef.current
      if (pc && pc.remoteDescription) await pc.addIceCandidate(new RTCIceCandidate(candidate))
      else pendingCandidates.current.push(candidate)
    })

    conn.on('CallRejected', () => { cleanupMedia(); setCall(null) })
    conn.on('CallEnded', () => { cleanupMedia(); setCall(null) })

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
      call, localStream, remoteStream, muted, camOff,
      startCall, acceptCall, rejectCall, endCall, toggleMute, toggleCam,
    }}>
      {children}
    </RealtimeContext.Provider>
  )
}

export const useRealtime = () => useContext(RealtimeContext)
