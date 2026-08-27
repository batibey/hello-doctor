import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import api from '../api/client'
import { useAuth } from '../context/AuthContext'
import { useRealtime } from '../context/RealtimeContext'
import { Avatar, Icon, clockTime } from '../components/ui'
import { encryptMessage, decryptMessage } from '../crypto/keys'

export default function ChatScreen() {
  const { userId } = useParams()
  const nav = useNavigate()
  const { user, privateKey } = useAuth()
  const { sendMessage, sendTyping, onMessage, onTyping, startCall, connected } = useRealtime()
  const [peer, setPeer] = useState(null)
  const [messages, setMessages] = useState([])
  const [text, setText] = useState('')
  const [peerTyping, setPeerTyping] = useState(false)
  const [sendError, setSendError] = useState(null)
  const scrollRef = useRef(null)
  const typingTimer = useRef(null)

  // Şifreli mesajın düz metni yalnızca bellekte tutulur; id -> metin.
  // null değer "çözülemedi" demek (anahtar yok ya da şifre sıfırlanmış).
  const [plain, setPlain] = useState({})

  const decryptInto = useCallback(async (list) => {
    if (!list.length) return
    const entries = await Promise.all(list.map(async (m) => [
      m.id, await decryptMessage(m, privateKey, m.senderId === user.id),
    ]))
    setPlain((prev) => {
      const next = { ...prev }
      for (const [id, value] of entries) next[id] = value
      return next
    })
  }, [privateKey, user.id])

  useEffect(() => {
    api.get(`/users/${userId}`).then(({ data }) => setPeer(data))
    api.get(`/messages/${userId}`).then(({ data }) => {
      setMessages(data)
      decryptInto(data)
    })
  }, [userId, decryptInto])

  useEffect(() => {
    const offMsg = onMessage((m) => {
      if (m.senderId === userId || m.recipientId === userId) {
        setMessages((prev) => prev.some((x) => x.id === m.id) ? prev : [...prev, m])
        decryptInto([m])
      }
    })
    const offTyping = onTyping((fromId, isTyping) => { if (fromId === userId) setPeerTyping(isTyping) })
    return () => { offMsg(); offTyping() }
  }, [userId, onMessage, onTyping, decryptInto])

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages, peerTyping])

  const send = async () => {
    const t = text.trim()
    if (!t) return

    // Karşı tarafın açık anahtarı yoksa (hesabı henüz giriş yapmamış) şifreli
    // gönderemeyiz. Sessizce düz metne düşmek yerine engelliyoruz — kullanıcı
    // şifreli sandığı bir mesajı açıkta göndermiş olmasın.
    if (!peer?.publicKey || !user?.publicKey) {
      setSendError('Şifreleme anahtarı hazır değil. Karşı taraf henüz giriş yapmamış olabilir.')
      return
    }

    // Girdi ancak sunucu mesajı kabul ettikten sonra temizlenir; aksi halde
    // bağlantı kopukken yazılan mesaj hiçbir iz bırakmadan kayboluyordu.
    setText('')
    setSendError(null)
    try {
      const payload = await encryptMessage(t, user.publicKey, peer.publicKey)
      await sendMessage(userId, payload)
      sendTyping(userId, false)
    } catch {
      setText(t)
      setSendError('Mesaj gönderilemedi. Bağlantı kurulunca tekrar deneyin.')
    }
  }

  const onType = (v) => {
    setText(v)
    sendTyping(userId, true)
    clearTimeout(typingTimer.current)
    typingTimer.current = setTimeout(() => sendTyping(userId, false), 1500)
  }

  if (!peer) return null

  return (
    <div className="screen">
      {/* Header */}
      <div className="glass" style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '16px 16px 12px', borderBottom: '1px solid var(--border)' }}>
        <button onClick={() => nav('/messages')} style={{ padding: 4 }}><Icon name="back" size={22} /></button>
        <Avatar name={peer.fullName} color={peer.avatarColor} size={42} />
        <div style={{ flex: 1 }}>
          <div style={{ fontWeight: 700, fontSize: 15.5 }}>{peer.title ? `${peer.title} ` : ''}{peer.fullName}</div>
          <div style={{ fontSize: 12, color: peerTyping ? 'var(--mint)' : 'var(--text-faint)' }}>
            {peerTyping ? 'yazıyor…' : connected ? 'çevrimiçi' : 'bağlanıyor…'}
          </div>
        </div>
        <button className="btn" style={{ padding: 10, borderRadius: 12 }} onClick={() => startCall(peer, 'voice')}><Icon name="phone" size={19} /></button>
        <button className="btn primary" style={{ padding: 10, borderRadius: 12 }} onClick={() => startCall(peer, 'video')}><Icon name="video" size={19} /></button>
      </div>

      {/* Messages */}
      <div ref={scrollRef} className="screen-scroll no-nav" style={{ display: 'flex', flexDirection: 'column', gap: 8, paddingTop: 16 }}>
        <div style={{ textAlign: 'center', margin: '4px 0 10px' }}>
          <span className="pill" style={{ fontSize: 11 }}><Icon name="shield" size={12} /> Uçtan uca güvenli sağlık görüşmesi</span>
        </div>
        {messages.map((m) => {
          const mine = m.senderId === user.id
          const body = m.id in plain ? plain[m.id] : undefined
          const unreadable = body === null
          return (
            <div key={m.id} className="pop" style={{ alignSelf: mine ? 'flex-end' : 'flex-start', maxWidth: '78%' }}>
              <div style={{
                padding: '10px 14px', borderRadius: 18, fontSize: 14.5, lineHeight: 1.45,
                background: mine ? 'var(--grad-brand)' : 'var(--surface-2)',
                color: '#fff', border: mine ? 'none' : '1px solid var(--border)',
                borderBottomRightRadius: mine ? 5 : 18, borderBottomLeftRadius: mine ? 18 : 5,
                opacity: unreadable ? 0.65 : 1,
                fontStyle: unreadable ? 'italic' : 'normal',
              }}>
                {/* undefined: çözme sürüyor, null: çözülemedi */}
                {body === undefined ? '…' : unreadable
                  ? 'Bu mesaj açılamıyor — şifre sıfırlandığında eski anahtar kaybolur.'
                  : body}
              </div>
              <div className="faint" style={{ fontSize: 10.5, marginTop: 3, textAlign: mine ? 'right' : 'left', padding: '0 6px' }}>{clockTime(m.sentAt)}</div>
            </div>
          )
        })}
        {peerTyping && (
          <div style={{ alignSelf: 'flex-start', padding: '12px 16px', borderRadius: 18, background: 'var(--surface-2)', display: 'flex', gap: 4 }}>
            {[0, 1, 2].map((i) => <span key={i} style={{ width: 7, height: 7, borderRadius: '50%', background: 'var(--text-dim)', animation: `typing-bounce 1.2s ${i * 0.15}s infinite` }} />)}
          </div>
        )}
      </div>

      {sendError && (
        <div style={{
          padding: '9px 16px', fontSize: 12.5, textAlign: 'center',
          background: 'rgba(251,113,133,.12)', color: '#fda4af',
          borderTop: '1px solid rgba(251,113,133,.28)',
        }}>{sendError}</div>
      )}

      {/* Composer */}
      <div className="glass" style={{ display: 'flex', gap: 10, padding: '12px 14px 16px', borderTop: '1px solid var(--border)' }}>
        <input className="input" placeholder="Mesaj yazın…" value={text}
          onChange={(e) => onType(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && send()} />
        <button className="btn primary" style={{ padding: '0 18px', borderRadius: 16 }} onClick={send}><Icon name="send" size={20} /></button>
      </div>
    </div>
  )
}
