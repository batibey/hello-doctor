import { useLocation, useNavigate } from 'react-router-dom'
import { Icon } from './ui'

export default function BottomNav({ unread = 0 }) {
  const nav = useNavigate()
  const { pathname } = useLocation()

  const items = [
    { to: '/', icon: 'home', label: 'Ana Sayfa' },
    { to: '/appointments', icon: 'calendar', label: 'Randevular' },
    { to: '/messages', icon: 'chat', label: 'Mesajlar', badge: unread },
    { to: '/profile', icon: 'user', label: 'Profil' },
  ]

  return (
    <nav className="bottom-nav">
      {items.map((it) => {
        const active = it.to === '/' ? pathname === '/' : pathname.startsWith(it.to)
        return (
          <button key={it.to} className={`nav-item ${active ? 'active' : ''}`} onClick={() => nav(it.to)} style={{ position: 'relative' }}>
            <div className="nav-icon"><Icon name={it.icon} size={19} /></div>
            {it.label}
            {it.badge > 0 && <span className="nav-badge">{it.badge > 9 ? '9+' : it.badge}</span>}
          </button>
        )
      })}
    </nav>
  )
}
