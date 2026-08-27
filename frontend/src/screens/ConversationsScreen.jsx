import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import api from '../api/client'
import { useAuth } from '../context/AuthContext'
import { useRealtime } from '../context/RealtimeContext'
import { Avatar, Icon, Loader, timeAgo } from '../components/ui'
import { decryptMessage } from '../crypto/keys'

export default function ConversationsScreen() {
  const nav = useNavigate()
  const { isDoctor, user, privateKey } = useAuth()
  const { onMessage } = useRealtime()
  const [convs, setConvs] = useState(null)
  const [doctors, setDoctors] = useState([])

  // Son mesaj önizlemesi de şifreli geliyor; sunucu çözemediği için burada
  // çözülüp listeye yazılıyor.
  const load = useCallback(async () => {
    try {
      const { data } = await api.get('/messages/conversations')
      const withPreview = await Promise.all(data.map(async (c) => ({
        ...c,
        preview: await decryptMessage(
          c.lastMessage, privateKey, c.lastMessage.senderId === user.id),
      })))
      setConvs(withPreview)
    } catch {
      setConvs([])
    }
  }, [privateKey, user.id])

  useEffect(() => {
    load()
    if (!isDoctor) api.get('/users/doctors').then(({ data }) => setDoctors(data)).catch(() => {})
    const off = onMessage(() => load())
    return off
  }, [isDoctor, onMessage, load])

  return (
    <div className="screen">
      <div className="screen-scroll">
        <div className="h2" style={{ marginBottom: 16 }}>Mesajlar</div>

        {/* Patient quick-start: chat with any doctor */}
        {!isDoctor && doctors.length > 0 && (
          <>
            <div className="section-title" style={{ marginBottom: 10 }}>Doktorla Görüş</div>
            <div className="row" style={{ gap: 12, overflowX: 'auto', paddingBottom: 6, marginBottom: 18 }}>
              {doctors.map((d) => (
                <button key={d.id} onClick={() => nav(`/chat/${d.id}`)} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, minWidth: 66 }}>
                  <div className="pulse" style={{ borderRadius: '50%' }}><Avatar name={d.fullName} color={d.avatarColor} size={58} /></div>
                  <div style={{ fontSize: 11, textAlign: 'center', lineHeight: 1.2 }}>{d.fullName.split(' ')[0]}</div>
                </button>
              ))}
            </div>
          </>
        )}

        <div className="section-title" style={{ marginBottom: 10 }}>Sohbetler</div>
        {convs === null ? <Loader /> : convs.length === 0 ? (
          <div className="card" style={{ textAlign: 'center', padding: 28, color: 'var(--text-dim)' }}>
            <Icon name="chat" size={30} /><div style={{ marginTop: 10, fontSize: 14 }}>Henüz mesajınız yok</div>
          </div>
        ) : (
          <div className="stack" style={{ gap: 4 }}>
            {convs.map((c) => (
              <button key={c.userId} className="row fade-up" style={{ width: '100%', textAlign: 'left', padding: '12px 8px', borderRadius: 16, gap: 13 }} onClick={() => nav(`/chat/${c.userId}`)}>
                <div style={{ position: 'relative' }}>
                  <Avatar name={c.fullName} color={c.avatarColor} size={52} />
                  <span style={{ position: 'absolute', bottom: 2, right: 2, width: 12, height: 12, borderRadius: '50%', background: 'var(--mint)', border: '2px solid var(--bg-2)' }} />
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="spread"><span style={{ fontWeight: 700, fontSize: 15 }}>{c.fullName}</span><span className="faint" style={{ fontSize: 11.5 }}>{timeAgo(c.lastAt)}</span></div>
                  <div className="spread" style={{ marginTop: 3 }}>
                    <span className="dim" style={{ fontSize: 13, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: 220, fontStyle: c.preview === null ? 'italic' : 'normal' }}>
                      {c.preview === null ? 'Şifreli mesaj' : c.preview}
                    </span>
                    {c.unread > 0 && <span className="nav-badge" style={{ position: 'static' }}>{c.unread}</span>}
                  </div>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
