import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import api from '../api/client'
import { useAuth } from '../context/AuthContext'
import { Avatar, Icon, Loader, clockTime } from '../components/ui'

export default function HomeScreen() {
  const { user, isDoctor } = useAuth()
  const nav = useNavigate()
  const [appts, setAppts] = useState(null)
  const [doctors, setDoctors] = useState([])
  const [q, setQ] = useState('')

  useEffect(() => {
    api.get('/appointments').then(({ data }) => setAppts(data)).catch(() => setAppts([]))
    if (!isDoctor) api.get('/users/doctors').then(({ data }) => setDoctors(data)).catch(() => {})
  }, [isDoctor])

  const upcoming = (appts || []).filter((a) => a.status !== 'Cancelled' && a.status !== 'Completed').slice(0, 3)
  const filteredDoctors = doctors.filter((d) =>
    !q || d.fullName.toLowerCase().includes(q.toLowerCase()) || (d.specialty || '').toLowerCase().includes(q.toLowerCase()))

  const firstName = user?.fullName?.split(' ')[0] || ''
  const hour = new Date().getHours()
  const greeting = hour < 12 ? 'Günaydın' : hour < 18 ? 'İyi günler' : 'İyi akşamlar'

  return (
    <div className="screen">
      <div className="screen-scroll">
        {/* Header */}
        <div className="spread fade-up" style={{ marginBottom: 22 }}>
          <div>
            <div className="dim" style={{ fontSize: 14 }}>{greeting},</div>
            <div className="h2">{isDoctor ? `${user.title || 'Dr.'} ${firstName}` : firstName} 👋</div>
          </div>
          <div className="row" style={{ gap: 10 }}>
            <button className="btn" style={{ padding: 11, borderRadius: 14 }}><Icon name="bell" size={19} /></button>
            <Avatar name={user?.fullName} color={user?.avatarColor} size={44} />
          </div>
        </div>

        {/* Hero card */}
        <div className="fade-up" style={{ background: 'var(--grad-brand)', borderRadius: 'var(--radius)', padding: 20, boxShadow: 'var(--shadow-brand)', marginBottom: 24, position: 'relative', overflow: 'hidden' }}>
          <div style={{ position: 'absolute', right: -30, top: -30, width: 140, height: 140, borderRadius: '50%', background: 'rgba(255,255,255,.12)' }} />
          <div style={{ position: 'relative', zIndex: 1 }}>
            <div className="pill" style={{ background: 'rgba(255,255,255,.2)', color: '#fff', border: 'none' }}>
              <Icon name="shield" size={13} /> {isDoctor ? 'Doktor Paneli' : 'Sağlığınız güvende'}
            </div>
            <div style={{ fontSize: 20, fontWeight: 800, marginTop: 12, lineHeight: 1.3 }}>
              {isDoctor ? 'Hastalarınızla anında\ngörüntülü görüşün' : 'Uzman doktorlarla\ngörüntülü randevu alın'}
            </div>
            <button onClick={() => nav(isDoctor ? '/appointments' : '/messages')} className="btn" style={{ marginTop: 16, background: '#fff', color: '#4338CA', fontWeight: 700, padding: '11px 18px' }}>
              {isDoctor ? 'Randevularımı Gör' : 'Hemen Başla'} <Icon name="back" size={16} style={{ transform: 'rotate(180deg)' }} />
            </button>
          </div>
        </div>

        {/* Upcoming appointments */}
        <div className="spread" style={{ marginBottom: 12 }}>
          <div className="section-title">Yaklaşan Randevular</div>
          <button className="faint" style={{ fontSize: 12.5, fontWeight: 600 }} onClick={() => nav('/appointments')}>Tümü</button>
        </div>

        {appts === null ? <Loader /> : upcoming.length === 0 ? (
          <div className="card fade-up" style={{ textAlign: 'center', padding: 24, color: 'var(--text-dim)' }}>
            <Icon name="calendar" size={28} /><div style={{ marginTop: 8, fontSize: 14 }}>Yaklaşan randevu yok</div>
          </div>
        ) : (
          <div className="stack" style={{ gap: 10 }}>
            {upcoming.map((a) => (
              <ApptCard key={a.id} a={a} isDoctor={isDoctor} onClick={() => nav('/appointments')} />
            ))}
          </div>
        )}

        {/* Patient: browse doctors */}
        {!isDoctor && (
          <>
            <div className="section-title" style={{ margin: '26px 0 12px' }}>Doktor Bul</div>
            <div style={{ position: 'relative', marginBottom: 14 }}>
              <div style={{ position: 'absolute', left: 15, top: 15, color: 'var(--text-faint)' }}><Icon name="search" size={18} /></div>
              <input className="input" style={{ paddingLeft: 44 }} placeholder="Uzmanlık veya isim ara…" value={q} onChange={(e) => setQ(e.target.value)} />
            </div>
            <div className="stack" style={{ gap: 10 }}>
              {filteredDoctors.map((d) => (
                <button key={d.id} className="card row fade-up" style={{ width: '100%', textAlign: 'left' }} onClick={() => nav(`/doctor/${d.id}`)}>
                  <Avatar name={d.fullName} color={d.avatarColor} size={52} />
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 700, fontSize: 15 }}>{d.title} {d.fullName}</div>
                    <div className="dim" style={{ fontSize: 13 }}>{d.specialty}</div>
                  </div>
                  <div className="pill amber"><Icon name="star" size={12} /> {d.rating}</div>
                </button>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function ApptCard({ a, isDoctor, onClick }) {
  const who = isDoctor ? a.patientName : `${a.doctorName}`
  const sub = isDoctor ? (a.reason || 'Muayene') : a.doctorSpecialty
  const d = new Date(a.scheduledAt)
  const typeMap = { Video: ['video', 'brand'], Voice: ['phone', 'mint'], Message: ['chat', 'amber'] }
  const [icon, cls] = typeMap[a.type] || ['video', 'brand']
  const statusMap = { Pending: ['Beklemede', 'amber'], Confirmed: ['Onaylı', 'mint'], Completed: ['Tamamlandı', ''], Cancelled: ['İptal', 'rose'] }
  const [stLabel, stCls] = statusMap[a.status]
  return (
    <button className="card row fade-up" style={{ width: '100%', textAlign: 'left', gap: 14 }} onClick={onClick}>
      <div style={{ width: 48, textAlign: 'center' }}>
        <div style={{ fontSize: 20, fontWeight: 800 }}>{d.getDate()}</div>
        <div className="faint" style={{ fontSize: 11, textTransform: 'uppercase' }}>{d.toLocaleDateString('tr-TR', { month: 'short' })}</div>
      </div>
      <div style={{ width: 1, alignSelf: 'stretch', background: 'var(--border)' }} />
      <div style={{ flex: 1 }}>
        <div style={{ fontWeight: 700, fontSize: 15 }}>{who}</div>
        <div className="dim row" style={{ fontSize: 12.5, gap: 6, marginTop: 3 }}><Icon name="clock" size={13} /> {clockTime(a.scheduledAt)} · {sub}</div>
      </div>
      <div className="stack" style={{ alignItems: 'flex-end', gap: 6 }}>
        <div className={`pill ${cls}`}><Icon name={icon} size={12} /></div>
        <span className={`pill ${stCls}`} style={{ fontSize: 10.5, padding: '3px 8px' }}>{stLabel}</span>
      </div>
    </button>
  )
}
