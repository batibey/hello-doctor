import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import api from '../api/client'
import { Avatar, Icon, Loader } from '../components/ui'

const TYPES = [
  ['Video', 'Görüntülü', 'video'],
  ['Voice', 'Sesli', 'phone'],
  ['Message', 'Mesaj', 'chat'],
]

export default function DoctorProfileScreen() {
  const { id } = useParams()
  const nav = useNavigate()
  const [doc, setDoc] = useState(null)
  const [type, setType] = useState('Video')
  const [day, setDay] = useState(0)
  const [slot, setSlot] = useState('10:00')
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [done, setDone] = useState(false)

  useEffect(() => { api.get(`/users/${id}`).then(({ data }) => setDoc(data)) }, [id])

  if (!doc) return <div className="screen"><div className="screen-scroll no-nav"><Loader /></div></div>

  const days = Array.from({ length: 5 }, (_, i) => { const d = new Date(); d.setDate(d.getDate() + i); return d })
  const slots = ['09:00', '10:00', '11:00', '13:30', '14:30', '16:00']

  const book = async () => {
    setSaving(true)
    const d = new Date(days[day]); const [h, m] = slot.split(':')
    d.setHours(+h, +m, 0, 0)
    try {
      await api.post('/appointments', { doctorId: doc.id, scheduledAt: d.toISOString(), type, reason })
      setDone(true)
      setTimeout(() => nav('/appointments'), 1400)
    } catch { setSaving(false) }
  }

  if (done) return (
    <div className="screen"><div className="screen-scroll no-nav" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
      <div className="pop" style={{ width: 90, height: 90, borderRadius: '50%', background: 'var(--grad-mint)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#04211b' }}><Icon name="check" size={46} /></div>
      <div className="h2" style={{ marginTop: 18 }}>Randevu Oluşturuldu</div>
      <div className="dim" style={{ marginTop: 6 }}>{doc.title} {doc.fullName} · {slot}</div>
    </div></div>
  )

  return (
    <div className="screen">
      <div className="screen-scroll no-nav">
        <div className="row" style={{ marginBottom: 16 }}>
          <button className="btn" style={{ padding: 10, borderRadius: 12 }} onClick={() => nav(-1)}><Icon name="back" /></button>
          <div className="h2">Randevu Al</div>
        </div>

        {/* Doctor header */}
        <div className="card fade-up" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center', gap: 4, padding: 22 }}>
          <Avatar name={doc.fullName} color={doc.avatarColor} size={84} />
          <div className="h2" style={{ marginTop: 10 }}>{doc.title} {doc.fullName}</div>
          <div className="pill brand">{doc.specialty}</div>
          <div className="row" style={{ gap: 18, marginTop: 12 }}>
            <Stat icon="star" value={doc.rating} label="Puan" />
            <Stat icon="shield" value={`${doc.experienceYears}y`} label="Deneyim" />
            <Stat icon="heart" value="1.2k" label="Hasta" />
          </div>
          {doc.bio && <p className="dim" style={{ fontSize: 13.5, marginTop: 14, lineHeight: 1.6 }}>{doc.bio}</p>}
        </div>

        {/* Type */}
        <div className="section-title" style={{ margin: '22px 0 10px' }}>Görüşme Tipi</div>
        <div className="row" style={{ gap: 10 }}>
          {TYPES.map(([val, label, icon]) => (
            <button key={val} onClick={() => setType(val)} className="card" style={{ flex: 1, textAlign: 'center', padding: '14px 6px', border: type === val ? '1px solid var(--brand)' : '1px solid var(--border)', background: type === val ? 'rgba(99,102,241,.14)' : 'var(--surface)' }}>
              <Icon name={icon} size={22} />
              <div style={{ fontSize: 12.5, fontWeight: 600, marginTop: 6 }}>{label}</div>
            </button>
          ))}
        </div>

        {/* Day */}
        <div className="section-title" style={{ margin: '22px 0 10px' }}>Gün Seçin</div>
        <div className="row" style={{ gap: 8, overflowX: 'auto', paddingBottom: 4 }}>
          {days.map((d, i) => (
            <button key={i} onClick={() => setDay(i)} className="card" style={{ minWidth: 58, textAlign: 'center', padding: '12px 4px', border: day === i ? '1px solid var(--brand)' : '1px solid var(--border)', background: day === i ? 'var(--grad-brand)' : 'var(--surface)' }}>
              <div className="faint" style={{ fontSize: 11, color: day === i ? 'rgba(255,255,255,.8)' : undefined }}>{d.toLocaleDateString('tr-TR', { weekday: 'short' })}</div>
              <div style={{ fontSize: 19, fontWeight: 800, color: day === i ? '#fff' : undefined }}>{d.getDate()}</div>
            </button>
          ))}
        </div>

        {/* Slot */}
        <div className="section-title" style={{ margin: '22px 0 10px' }}>Saat Seçin</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 8 }}>
          {slots.map((s) => (
            <button key={s} onClick={() => setSlot(s)} className="btn" style={{ padding: 12, background: slot === s ? 'var(--grad-brand)' : 'var(--surface)', border: slot === s ? 'none' : '1px solid var(--border)', color: '#fff' }}>{s}</button>
          ))}
        </div>

        <div className="section-title" style={{ margin: '22px 0 10px' }}>Şikayet (opsiyonel)</div>
        <textarea className="input" rows={2} placeholder="Örn. Göğüs ağrısı, tansiyon takibi…" value={reason} onChange={(e) => setReason(e.target.value)} style={{ resize: 'none' }} />

        <button className="btn primary block" style={{ marginTop: 22, height: 54 }} onClick={book} disabled={saving}>
          {saving ? <div className="spinner" /> : <>Randevuyu Onayla · {days[day].toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' })} {slot}</>}
        </button>
      </div>
    </div>
  )
}

function Stat({ icon, value, label }) {
  return (
    <div style={{ textAlign: 'center' }}>
      <div className="row" style={{ gap: 4, justifyContent: 'center', fontWeight: 800, fontSize: 16 }}><Icon name={icon} size={14} /> {value}</div>
      <div className="faint" style={{ fontSize: 11 }}>{label}</div>
    </div>
  )
}
