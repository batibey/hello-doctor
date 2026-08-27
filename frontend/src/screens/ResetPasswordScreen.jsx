import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { Icon } from '../components/ui'
import { cryptoAvailable } from '../crypto/keys'

const MIN_PASSWORD = 8

export default function ResetPasswordScreen() {
  const { resetPassword } = useAuth()
  const nav = useNavigate()
  const [params] = useSearchParams()
  const token = params.get('token') || ''

  const [password, setPassword] = useState('')
  const [password2, setPassword2] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async (e) => {
    e.preventDefault()
    setError('')

    if (password.length < MIN_PASSWORD)
      return setError(`Şifre en az ${MIN_PASSWORD} karakter olmalı.`)
    if (password !== password2)
      return setError('Şifreler eşleşmiyor.')
    if (!cryptoAvailable())
      return setError('Tarayıcınız şifreleme desteklemiyor. HTTPS üzerinden bağlanın.')

    setLoading(true)
    try {
      await resetPassword(token, password)
      nav('/', { replace: true })
    } catch (err) {
      setError(err?.response?.data?.message || 'Şifre sıfırlanamadı.')
    } finally {
      setLoading(false)
    }
  }

  if (!token) {
    return (
      <div className="screen">
        <div className="screen-scroll no-nav" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <div className="card" style={{ textAlign: 'center', padding: '22px 18px' }}>
            <div style={{ fontWeight: 700, marginBottom: 8 }}>Bağlantı geçersiz</div>
            <div className="dim" style={{ fontSize: 13, lineHeight: 1.6 }}>
              Sıfırlama bağlantısı eksik görünüyor. E-postadaki bağlantıyı olduğu gibi açın.
            </div>
          </div>
          <div style={{ textAlign: 'center', marginTop: 18, fontSize: 13 }} className="dim">
            <Link to="/forgot-password" style={{ color: 'var(--brand)', fontWeight: 600 }}>Yeni bağlantı iste</Link>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="screen">
      <div className="screen-scroll no-nav" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', paddingTop: 40 }}>
        <div className="fade-up" style={{ textAlign: 'center', marginBottom: 24 }}>
          <h1 className="h1" style={{ fontSize: 26 }}>Yeni şifre</h1>
        </div>

        <div className="card fade-up" style={{ marginBottom: 16, fontSize: 12.5, lineHeight: 1.6 }}>
          <Icon name="shield" size={13} /> Yeni bir şifreleme anahtarı oluşturulacak.
          Eski şifreniz bilinmediği için önceki anahtar açılamıyor —
          <b> bu sıfırlamadan önceki mesajlarınız okunamaz hale gelecek.</b>
        </div>

        <form onSubmit={submit} className="fade-up" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <input className="input" type="password" placeholder={`Yeni şifre (en az ${MIN_PASSWORD} karakter)`}
            value={password} onChange={(e) => setPassword(e.target.value)} required autoComplete="new-password" />
          <input className="input" type="password" placeholder="Yeni şifre tekrar"
            value={password2} onChange={(e) => setPassword2(e.target.value)} required autoComplete="new-password" />

          {error && <div className="pill rose" style={{ justifyContent: 'center' }}>{error}</div>}

          <button className="btn primary block" type="submit" disabled={loading} style={{ height: 52 }}>
            {loading ? <div className="spinner" /> : 'Şifreyi Değiştir'}
          </button>
        </form>

        <div style={{ textAlign: 'center', marginTop: 20, fontSize: 13 }} className="dim">
          <Link to="/" style={{ color: 'var(--brand)', fontWeight: 600 }}>Girişe dön</Link>
        </div>
      </div>
    </div>
  )
}
