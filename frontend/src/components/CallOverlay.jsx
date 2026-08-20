import { useEffect, useRef, useState } from 'react'
import { useRealtime } from '../context/RealtimeContext'
import { Avatar, Icon } from './ui'

export default function CallOverlay() {
  const { call, localStream, remoteStream, muted, camOff, acceptCall, rejectCall, endCall, toggleMute, toggleCam } = useRealtime()
  const localRef = useRef(null)
  const remoteRef = useRef(null)
  const [seconds, setSeconds] = useState(0)

  useEffect(() => { if (localRef.current && localStream) localRef.current.srcObject = localStream }, [localStream])
  useEffect(() => { if (remoteRef.current && remoteStream) remoteRef.current.srcObject = remoteStream }, [remoteStream])

  useEffect(() => {
    if (call?.phase !== 'active') { setSeconds(0); return }
    const t = setInterval(() => setSeconds((s) => s + 1), 1000)
    return () => clearInterval(t)
  }, [call?.phase])

  if (!call) return null
  const isVideo = call.callType === 'video'
  const active = call.phase === 'active'
  const mmss = `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`

  const statusText = {
    'ringing-out': 'Aranıyor…',
    'ringing-in': `Gelen ${isVideo ? 'görüntülü' : 'sesli'} arama`,
    'connecting': 'Bağlanıyor…',
    'active': mmss,
  }[call.phase]

  return (
    <div style={overlay}>
      {/* Remote video fills screen when video + active */}
      {isVideo && active && (
        <video ref={remoteRef} autoPlay playsInline style={remoteVideo} />
      )}

      {/* Ambient gradient when no remote video yet */}
      {!(isVideo && active) && <div style={ambient(call.peerColor)} />}

      {/* Local PiP */}
      {isVideo && localStream && (
        <video ref={localRef} autoPlay playsInline muted style={pip(camOff)} />
      )}
      {/* hidden audio-only remote playback */}
      {!isVideo && <video ref={remoteRef} autoPlay playsInline style={{ display: 'none' }} />}

      <div style={topInfo}>
        {!(isVideo && active) && (
          <div className={call.phase.startsWith('ringing') ? 'pulse' : ''} style={{ borderRadius: '50%', marginBottom: 22 }}>
            <Avatar name={call.peerName} color={call.peerColor} size={120} />
          </div>
        )}
        <div style={{ fontSize: 26, fontWeight: 800, letterSpacing: '-.4px' }}>{call.peerName}</div>
        <div style={{ marginTop: 8, color: 'var(--text-dim)', fontSize: 15, display: 'flex', alignItems: 'center', gap: 8 }}>
          <Icon name={isVideo ? 'video' : 'phone'} size={16} /> {statusText}
        </div>
      </div>

      <div style={controls}>
        {call.phase === 'ringing-in' ? (
          <>
            <CallBtn color="var(--grad-rose)" onClick={rejectCall} icon="phone" rotate label="Reddet" />
            <CallBtn color="var(--grad-mint)" onClick={acceptCall} icon={isVideo ? 'video' : 'phone'} label="Kabul Et" />
          </>
        ) : (
          <>
            <RoundBtn active={muted} onClick={toggleMute} icon={muted ? 'micOff' : 'mic'} />
            {isVideo && <RoundBtn active={camOff} onClick={toggleCam} icon={camOff ? 'camOff' : 'video'} />}
            <CallBtn color="var(--grad-rose)" onClick={endCall} icon="phone" rotate label="Bitir" />
          </>
        )}
      </div>
    </div>
  )
}

function CallBtn({ color, onClick, icon, rotate, label }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
      <button onClick={onClick} style={{ ...bigBtn, background: color, transform: rotate ? 'rotate(135deg)' : 'none' }}>
        <Icon name={icon} size={28} />
      </button>
      <span style={{ fontSize: 12, color: 'var(--text-dim)', transform: rotate ? 'none' : 'none' }}>{label}</span>
    </div>
  )
}

function RoundBtn({ active, onClick, icon }) {
  return (
    <button onClick={onClick} style={{ ...bigBtn, width: 62, height: 62, background: active ? '#fff' : 'rgba(255,255,255,.14)', color: active ? '#0B1120' : '#fff', border: '1px solid rgba(255,255,255,.18)' }}>
      <Icon name={icon} size={24} />
    </button>
  )
}

const overlay = { position: 'absolute', inset: 0, zIndex: 100, background: '#05070d', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'space-between', padding: '72px 24px 54px', overflow: 'hidden', animation: 'pop .25s ease' }
const remoteVideo = { position: 'absolute', inset: 0, width: '100%', height: '100%', objectFit: 'cover', background: '#000' }
const ambient = (c) => ({ position: 'absolute', inset: 0, background: `radial-gradient(700px 500px at 50% 25%, ${c}44, transparent 60%), #05070d` })
const pip = (off) => ({ position: 'absolute', top: 20, right: 20, width: 104, height: 150, objectFit: 'cover', borderRadius: 18, border: '2px solid rgba(255,255,255,.25)', background: '#111', zIndex: 5, opacity: off ? 0.25 : 1 })
const topInfo = { position: 'relative', zIndex: 3, display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center', marginTop: 20 }
const controls = { position: 'relative', zIndex: 3, display: 'flex', alignItems: 'center', gap: 22 }
const bigBtn = { width: 70, height: 70, borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#fff', boxShadow: '0 10px 30px rgba(0,0,0,.4)' }
