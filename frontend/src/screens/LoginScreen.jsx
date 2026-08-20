import { useState } from 'react'
import { useAuth } from '../context/AuthContext'
import { Icon } from '../components/ui'

const DEMO = {
  Patient: { email: 'hasta@hellodoctor.com', password: '1234' },
  Doctor: { email: 'dr.ayse@hellodoctor.com', password: '1234' },
}

export default function LoginScreen() {
  const { login } = useAuth()
  const [role, setRole] = useState('Patient')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async (e) => {
    e.preventDefault()
    setError(''); setLoading(true)
    try {
      await login(email.trim(), password, role)
    } catch (err) {
      setError(err?.response?.data?.message || 'Giriş başarısız.')
    } finally {
      setLoading(false)
    }
  }

  const fillDemo = () => { setEmail(DEMO[role].email); setPassword(DEMO[role].password) }

  return (
    <div className="screen">
      <div className="screen-scroll no-nav" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', paddingTop: 40 }}>
        <div className="fade-up" style={{ textAlign: 'center', marginBottom: 30 }}>
          <div style={{ width: 74, height: 74, borderRadius: 22, margin: '0 auto 16px', background: 'var(--grad-brand)', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: 'var(--shadow-brand)' }}>
            <Icon name="heart" size={38} />
          </div>
          <h1 className="h1">Hello<span className="gradient-text">Doctor</span></h1>
          <p className="dim" style={{ marginTop: 6, fontSize: 14 }}>Doktorunuz bir dokunuş uzağınızda</p>
        </div>

        {/* Role switch */}
        <div className="fade-up" style={{ display: 'flex', gap: 8, padding: 5, borderRadius: 18, background: 'var(--surface)', border: '1px solid var(--border)', marginBottom: 22 }}>
          {[['Patient', 'Hasta', 'user'], ['Doctor', 'Doktor', 'shield']].map(([val, label, icon]) => (
            <button key={val} onClick={() => { setRole(val); setError('') }}
              style={{ flex: 1, padding: '12px', borderRadius: 14, fontWeight: 700, fontSize: 14, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8,
                background: role === val ? 'var(--grad-brand)' : 'transparent', color: role === val ? '#fff' : 'var(--text-dim)', boxShadow: role === val ? 'var(--shadow-brand)' : 'none', transition: 'all .2s' }}>
              <Icon name={icon} size={17} /> {label}
            </button>
          ))}
        </div>

        <form onSubmit={submit} className="fade-up" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <input className="input" type="email" placeholder="E-posta adresi" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="username" />
          <input className="input" type="password" placeholder="Şifre" value={password} onChange={(e) => setPassword(e.target.value)} required autoComplete="current-password" />

          {error && <div className="pill rose" style={{ justifyContent: 'center' }}>{error}</div>}

          <button className="btn primary block" type="submit" disabled={loading} style={{ marginTop: 6, height: 52 }}>
            {loading ? <div className="spinner" /> : <>{role === 'Doctor' ? 'Doktor Girişi' : 'Hasta Girişi'} <Icon name="back" size={18} style={{ transform: 'rotate(180deg)' }} /></>}
          </button>
        </form>

        <button onClick={fillDemo} className="btn ghost" style={{ marginTop: 14, fontSize: 13, color: 'var(--text-dim)' }}>
          Demo bilgilerini doldur ✨
        </button>

        <div className="card" style={{ marginTop: 18, fontSize: 12.5 }}>
          <div className="section-title" style={{ marginBottom: 8 }}>Demo Hesaplar</div>
          <div className="dim" style={{ lineHeight: 1.7 }}>
            <b style={{ color: 'var(--text)' }}>Hasta:</b> hasta@hellodoctor.com · 1234<br />
            <b style={{ color: 'var(--text)' }}>Doktor:</b> dr.ayse@hellodoctor.com · 1234
          </div>
        </div>
      </div>
    </div>
  )
}
