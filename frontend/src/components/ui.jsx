export function Avatar({ name, color = '#6366F1', size = 44, src }) {
  const initials = (name || '?')
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0])
    .join('')
    .toUpperCase()
  return (
    <div
      className="avatar"
      style={{
        width: size, height: size, fontSize: size * 0.38,
        background: src ? undefined : `linear-gradient(135deg, ${color}, ${shade(color, -25)})`,
      }}
    >
      {initials}
    </div>
  )
}

function shade(hex, percent) {
  const n = parseInt(hex.replace('#', ''), 16)
  let r = (n >> 16) + percent
  let g = ((n >> 8) & 0xff) + percent
  let b = (n & 0xff) + percent
  r = Math.max(0, Math.min(255, r)); g = Math.max(0, Math.min(255, g)); b = Math.max(0, Math.min(255, b))
  return `#${((r << 16) | (g << 8) | b).toString(16).padStart(6, '0')}`
}

export function Icon({ name, size = 20 }) {
  const p = { width: size, height: size, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' }
  const paths = {
    home: <path d="M3 10.5 12 3l9 7.5M5 9.5V21h14V9.5" />,
    calendar: <><rect x="3" y="4.5" width="18" height="17" rx="3" /><path d="M3 9h18M8 2.5v4M16 2.5v4" /></>,
    chat: <path d="M21 11.5a8.5 8.5 0 0 1-12.4 7.5L3 20.5l1.5-5A8.5 8.5 0 1 1 21 11.5Z" />,
    user: <><circle cx="12" cy="8" r="4" /><path d="M4 21c0-4 3.6-6.5 8-6.5S20 17 20 21" /></>,
    search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-3.2-3.2" /></>,
    phone: <path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2 4.2 2 2 0 0 1 4 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.8a16 16 0 0 0 6 6l1.2-1.2a2 2 0 0 1 2.1-.5c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.9 2Z" />,
    video: <><rect x="2" y="6" width="14" height="12" rx="3" /><path d="m22 8-6 4 6 4V8Z" /></>,
    send: <path d="M22 2 11 13M22 2l-7 20-4-9-9-4 20-7Z" />,
    back: <path d="m15 18-6-6 6-6" />,
    star: <path d="m12 2 3 6.5 7 .9-5 4.9 1.2 7L12 18l-6.4 3.3L6.8 14l-5-4.9 7-.9L12 2Z" />,
    mic: <><rect x="9" y="3" width="6" height="11" rx="3" /><path d="M5 11a7 7 0 0 0 14 0M12 18v3" /></>,
    micOff: <><path d="M15 9V6a3 3 0 0 0-5.9-.8M9 9v2a3 3 0 0 0 4.2 2.7M5 11a7 7 0 0 0 10.3 6.2M12 18v3M3 3l18 18" /></>,
    camOff: <><path d="M2 6h11a3 3 0 0 1 3 3v1.5l6-4.5v12l-3-2.2M2 2l20 20M2 6v9a3 3 0 0 0 3 3h6" /></>,
    logout: <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" />,
    plus: <path d="M12 5v14M5 12h14" />,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    check: <path d="M20 6 9 17l-5-5" />,
    heart: <path d="M12 21s-7.5-4.6-10-9.3C.4 8.4 2 4.5 5.6 4.5c2 0 3.4 1.2 4.4 2.6 1-1.4 2.4-2.6 4.4-2.6 3.6 0 5.2 3.9 3.6 7.2C19.5 16.4 12 21 12 21Z" />,
    shield: <path d="M12 2 4 5v6c0 5 3.4 9 8 11 4.6-2 8-6 8-11V5l-8-3Z" />,
    bell: <path d="M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0" />,
    pill: <><rect x="3" y="9" width="18" height="6" rx="3" transform="rotate(45 12 12)" /><path d="M8.5 8.5 15.5 15.5" /></>,
  }
  return <svg {...p}>{paths[name] || null}</svg>
}

export function Loader({ label }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 14, height: '100%', color: 'var(--text-dim)' }}>
      <div className="spinner" style={{ borderTopColor: 'var(--brand)' }} />
      {label && <span style={{ fontSize: 14 }}>{label}</span>}
    </div>
  )
}

export function timeAgo(iso) {
  const d = new Date(iso)
  const s = Math.floor((Date.now() - d.getTime()) / 1000)
  if (s < 60) return 'şimdi'
  if (s < 3600) return `${Math.floor(s / 60)} dk`
  if (s < 86400) return `${Math.floor(s / 3600)} sa`
  return d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' })
}

export function clockTime(iso) {
  return new Date(iso).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
