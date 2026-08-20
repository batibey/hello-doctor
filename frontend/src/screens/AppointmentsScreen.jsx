import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import api from '../api/client'
import { useAuth } from '../context/AuthContext'
import { useRealtime } from '../context/RealtimeContext'
import { Avatar, Icon, Loader, clockTime } from '../components/ui'

export default function AppointmentsScreen() {
  const { isDoctor } = useAuth()
  const { startCall } = useRealtime()
  const nav = useNavigate()
  const [appts, setAppts] = useState(null)
  const [tab, setTab] = useState('upcoming')

  const load = () => api.get('/appointments').then(({ data }) => setAppts(data)).catch(() => setAppts([]))
  useEffect(() => { load() }, [])

  const setStatus = async (id, status) => {
    await api.put(`/appointments/${id}/status`, { status })
    load()
  }

  const startCallWith = (a) => {
    const peer = isDoctor
      ? { id: a.patientId, fullName: a.patientName, avatarColor: '#F59E0B' }
      : { id: a.doctorId, fullName: a.doctorName, avatarColor: '#6366F1' }
    startCall(peer, a.type === 'Voice' ? 'voice' : 'video')
  }

  const list = (appts || []).filter((a) =>
    tab === 'upcoming' ? (a.status === 'Pending' || a.status === 'Confirmed') : (a.status === 'Completed' || a.status === 'Cancelled'))

  return (
    <div className="screen">
      <div className="screen-scroll">
        <div className="h2" style={{ marginBottom: 16 }}>Randevular</div>

        <div style={{ display: 'flex', gap: 6, padding: 5, borderRadius: 16, background: 'var(--surface)', border: '1px solid var(--border)', marginBottom: 18 }}>
          {[['upcoming', 'Yaklaşan'], ['past', 'Geçmiş']].map(([val, label]) => (
            <button key={val} onClick={() => setTab(val)} style={{ flex: 1, padding: 10, borderRadius: 12, fontWeight: 700, fontSize: 14, background: tab === val ? 'var(--grad-brand)' : 'transparent', color: tab === val ? '#fff' : 'var(--text-dim)' }}>{label}</button>
          ))}
        </div>

        {appts === null ? <Loader /> : list.length === 0 ? (
          <div className="card" style={{ textAlign: 'center', padding: 30, color: 'var(--text-dim)' }}>
            <Icon name="calendar" size={30} /><div style={{ marginTop: 10 }}>Bu sekmede randevu yok</div>
            {!isDoctor && tab === 'upcoming' && <button className="btn primary" style={{ marginTop: 16 }} onClick={() => nav('/')}>Doktor Bul</button>}
          </div>
        ) : (
          <div className="stack" style={{ gap: 12 }}>
            {list.map((a) => {
              const who = isDoctor ? a.patientName : `${a.doctorName}`
              const color = isDoctor ? '#F59E0B' : '#6366F1'
              const d = new Date(a.scheduledAt)
              const typeMap = { Video: ['video', 'Görüntülü'], Voice: ['phone', 'Sesli'], Message: ['chat', 'Mesaj'] }
              const [tIcon, tLabel] = typeMap[a.type]
              const statusMap = { Pending: ['Beklemede', 'amber'], Confirmed: ['Onaylı', 'mint'], Completed: ['Tamamlandı', ''], Cancelled: ['İptal', 'rose'] }
              const [stLabel, stCls] = statusMap[a.status]
              const callable = a.status === 'Confirmed' && (a.type === 'Video' || a.type === 'Voice')
              return (
                <div key={a.id} className="card fade-up">
                  <div className="row" style={{ gap: 14 }}>
                    <Avatar name={who} color={color} size={50} />
                    <div style={{ flex: 1 }}>
                      <div style={{ fontWeight: 700, fontSize: 15.5 }}>{who}</div>
                      <div className="dim" style={{ fontSize: 13 }}>{isDoctor ? (a.reason || 'Muayene') : a.doctorSpecialty}</div>
                    </div>
                    <span className={`pill ${stCls}`}>{stLabel}</span>
                  </div>
                  <div className="row" style={{ gap: 14, marginTop: 12, fontSize: 12.5, color: 'var(--text-dim)' }}>
                    <span className="row" style={{ gap: 5 }}><Icon name="calendar" size={14} /> {d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long' })}</span>
                    <span className="row" style={{ gap: 5 }}><Icon name="clock" size={14} /> {clockTime(a.scheduledAt)}</span>
                    <span className="row" style={{ gap: 5 }}><Icon name={tIcon} size={14} /> {tLabel}</span>
                  </div>

                  {tab === 'upcoming' && (
                    <div className="row" style={{ gap: 8, marginTop: 14 }}>
                      {isDoctor && a.status === 'Pending' ? (
                        <>
                          <button className="btn mint" style={{ flex: 1, padding: 11 }} onClick={() => setStatus(a.id, 'Confirmed')}><Icon name="check" size={16} /> Onayla</button>
                          <button className="btn" style={{ flex: 1, padding: 11 }} onClick={() => setStatus(a.id, 'Cancelled')}>Reddet</button>
                        </>
                      ) : (
                        <>
                          {callable && <button className="btn primary" style={{ flex: 1, padding: 11 }} onClick={() => startCallWith(a)}><Icon name={a.type === 'Voice' ? 'phone' : 'video'} size={16} /> {a.type === 'Voice' ? 'Ara' : 'Görüşme'}</button>}
                          <button className="btn" style={{ flex: callable ? 0 : 1, padding: 11 }} onClick={() => nav(`/chat/${isDoctor ? a.patientId : a.doctorId}`)}><Icon name="chat" size={16} /> {callable ? '' : 'Mesaj'}</button>
                          {a.status !== 'Cancelled' && <button className="btn" style={{ padding: 11 }} onClick={() => setStatus(a.id, 'Cancelled')}>İptal</button>}
                        </>
                      )}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
