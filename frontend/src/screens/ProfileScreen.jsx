import { useAuth } from '../context/AuthContext'
import { useNavigate } from 'react-router-dom'
import { Avatar, Icon } from '../components/ui'

export default function ProfileScreen() {
  const nav = useNavigate()
  const { user, logout, isDoctor } = useAuth()

  const rows = isDoctor
    ? [
        ['shield', 'Uzmanlık', user.specialty],
        ['star', 'Değerlendirme', `${user.rating} / 5.0`],
        ['clock', 'Deneyim', `${user.experienceYears} yıl`],
        ['user', 'Unvan', user.title],
      ]
    : [
        ['user', 'Yaş', user.age ? `${user.age}` : '—'],
        ['heart', 'Kan Grubu', user.bloodType || '—'],
        ['pill', 'İlaçlar', 'Tanımlı değil'],
        ['shield', 'Sigorta', 'Aktif'],
      ]

  return (
    <div className="screen">
      <div className="screen-scroll">
        <div className="h2" style={{ marginBottom: 18 }}>Profil</div>

        <div className="card fade-up" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center', padding: 26 }}>
          <Avatar name={user.fullName} color={user.avatarColor} size={92} />
          <div className="h2" style={{ marginTop: 12 }}>{isDoctor ? `${user.title} ` : ''}{user.fullName}</div>
          <div className="pill brand" style={{ marginTop: 8 }}>
            <Icon name={isDoctor ? 'shield' : 'user'} size={13} /> {isDoctor ? user.specialty : 'Hasta'}
          </div>
          <div className="dim" style={{ fontSize: 13, marginTop: 10 }}>{user.email}</div>
        </div>

        <div className="section-title" style={{ margin: '24px 0 10px' }}>{isDoctor ? 'Uzman Bilgileri' : 'Sağlık Bilgileri'}</div>
        <div className="card" style={{ padding: 6 }}>
          {rows.map(([icon, label, value], i) => (
            <div key={label} className="row" style={{ padding: '13px 12px', borderBottom: i < rows.length - 1 ? '1px solid var(--border)' : 'none' }}>
              <div style={{ width: 38, height: 38, borderRadius: 12, background: 'var(--surface-2)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--brand)' }}><Icon name={icon} size={18} /></div>
              <div style={{ flex: 1 }} className="dim">{label}</div>
              <div style={{ fontWeight: 600 }}>{value}</div>
            </div>
          ))}
        </div>

        <div className="section-title" style={{ margin: '24px 0 10px' }}>Ayarlar</div>
        <div className="card" style={{ padding: 6 }}>
          {[['shield', 'Verilerim ve KVKK', '/privacy'], ['bell', 'Bildirimler', null], ['heart', 'Sağlık Kayıtları', null]].map(([icon, label, to], i) => (
            <button key={label} className="row" onClick={() => to && nav(to)}
              style={{ width: '100%', padding: '13px 12px', borderBottom: i < 2 ? '1px solid var(--border)' : 'none' }}>
              <div style={{ width: 38, height: 38, borderRadius: 12, background: 'var(--surface-2)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}><Icon name={icon} size={18} /></div>
              <div style={{ flex: 1, textAlign: 'left' }}>{label}</div>
              <Icon name="back" size={18} style={{ transform: 'rotate(180deg)', color: 'var(--text-faint)' }} />
            </button>
          ))}
        </div>

        <button className="btn rose block" style={{ marginTop: 22, height: 50 }} onClick={logout}>
          <Icon name="logout" size={18} /> Çıkış Yap
        </button>
        <div className="faint" style={{ textAlign: 'center', fontSize: 11.5, marginTop: 16 }}>HelloDoctor · Demo Sürüm v1.0</div>
      </div>
    </div>
  )
}
