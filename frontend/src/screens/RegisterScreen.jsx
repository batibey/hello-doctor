import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { Icon } from '../components/ui'
import { cryptoAvailable } from '../crypto/keys'

const MIN_PASSWORD = 8

export default function RegisterScreen() {
  const { register } = useAuth()
  const nav = useNavigate()
  const [role, setRole] = useState('Patient')
  const [form, setForm] = useState({
    fullName: '', email: '', password: '', password2: '',
    age: '', bloodType: '', specialty: '', title: '', experienceYears: '',
  })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }))

  const submit = async (e) => {
    e.preventDefault()
    setError('')

    if (form.password.length < MIN_PASSWORD)
      return setError(`Şifre en az ${MIN_PASSWORD} karakter olmalı.`)
    if (form.password !== form.password2)
      return setError('Şifreler eşleşmiyor.')
    if (!cryptoAvailable())
      return setError('Tarayıcınız şifreleme desteklemiyor. HTTPS üzerinden bağlanın.')

    setLoading(true)
    try {
      await register({
        fullName: form.fullName,
        email: form.email,
        password: form.password,
        role,
        age: role === 'Patient' && form.age ? Number(form.age) : null,
        bloodType: role === 'Patient' ? form.bloodType || null : null,
        specialty: role === 'Doctor' ? form.specialty || null : null,
        title: role === 'Doctor' ? form.title || null : null,
        experienceYears: role === 'Doctor' && form.experienceYears ? Number(form.experienceYears) : null,
        bio: null,
      })
      nav('/', { replace: true })
    } catch (err) {
      setError(err?.response?.data?.message || 'Kayıt tamamlanamadı.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="screen">
      <div className="screen-scroll no-nav" style={{ paddingTop: 28 }}>
        <div className="fade-up" style={{ textAlign: 'center', marginBottom: 24 }}>
          <h1 className="h1" style={{ fontSize: 26 }}>Hesap oluştur</h1>
          <p className="dim" style={{ marginTop: 6, fontSize: 13.5 }}>
            Mesajlarınız uçtan uca şifrelenir
          </p>
        </div>

        <div className="fade-up" style={{ display: 'flex', gap: 8, padding: 5, borderRadius: 18, background: 'var(--surface)', border: '1px solid var(--border)', marginBottom: 18 }}>
          {[['Patient', 'Hasta', 'user'], ['Doctor', 'Doktor', 'shield']].map(([val, label, icon]) => (
            <button key={val} type="button" onClick={() => { setRole(val); setError('') }}
              style={{ flex: 1, padding: 12, borderRadius: 14, fontWeight: 700, fontSize: 14, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8,
                background: role === val ? 'var(--grad-brand)' : 'transparent', color: role === val ? '#fff' : 'var(--text-dim)', transition: 'all .2s' }}>
              <Icon name={icon} size={17} /> {label}
            </button>
          ))}
        </div>

        <form onSubmit={submit} className="fade-up" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <input className="input" placeholder="Ad soyad" value={form.fullName} onChange={set('fullName')} required autoComplete="name" />
          <input className="input" type="email" placeholder="E-posta adresi" value={form.email} onChange={set('email')} required autoComplete="username" />
          <input className="input" type="password" placeholder={`Şifre (en az ${MIN_PASSWORD} karakter)`} value={form.password} onChange={set('password')} required autoComplete="new-password" />
          <input className="input" type="password" placeholder="Şifre tekrar" value={form.password2} onChange={set('password2')} required autoComplete="new-password" />

          {role === 'Patient' ? (
            <div style={{ display: 'flex', gap: 12 }}>
              <input className="input" type="number" placeholder="Yaş" value={form.age} onChange={set('age')} min="0" max="130" style={{ flex: 1 }} />
              <input className="input" placeholder="Kan grubu" value={form.bloodType} onChange={set('bloodType')} style={{ flex: 1 }} />
            </div>
          ) : (
            <>
              <input className="input" placeholder="Uzmanlık (ör. Kardiyoloji)" value={form.specialty} onChange={set('specialty')} />
              <div style={{ display: 'flex', gap: 12 }}>
                <input className="input" placeholder="Unvan" value={form.title} onChange={set('title')} style={{ flex: 1 }} />
                <input className="input" type="number" placeholder="Deneyim (yıl)" value={form.experienceYears} onChange={set('experienceYears')} min="0" max="70" style={{ flex: 1 }} />
              </div>
            </>
          )}

          <div className="card" style={{ fontSize: 12, lineHeight: 1.6 }}>
            <Icon name="shield" size={13} /> Şifreniz mesaj anahtarınızı da korur. Şifrenizi
            sıfırlarsanız yeni bir anahtar oluşturulur ve <b>eski mesajlarınız okunamaz hale gelir</b>.
          </div>

          {error && <div className="pill rose" style={{ justifyContent: 'center' }}>{error}</div>}

          <button className="btn primary block" type="submit" disabled={loading} style={{ height: 52 }}>
            {loading ? <div className="spinner" /> : 'Hesap Oluştur'}
          </button>
        </form>

        <div style={{ textAlign: 'center', marginTop: 18, marginBottom: 20, fontSize: 13 }} className="dim">
          Zaten hesabınız var mı? <Link to="/" style={{ color: 'var(--brand)', fontWeight: 600 }}>Giriş yapın</Link>
        </div>
      </div>
    </div>
  )
}
