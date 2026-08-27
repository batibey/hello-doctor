import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { Icon } from '../components/ui'

export default function ForgotPasswordScreen() {
  const { forgotPassword } = useAuth()
  const [email, setEmail] = useState('')
  const [loading, setLoading] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState('')

  const submit = async (e) => {
    e.preventDefault()
    setError(''); setLoading(true)
    try {
      await forgotPassword(email)
      setSent(true)
    } catch {
      setError('İstek gönderilemedi. Bağlantınızı kontrol edip tekrar deneyin.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="screen">
      <div className="screen-scroll no-nav" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', paddingTop: 40 }}>
        <div className="fade-up" style={{ textAlign: 'center', marginBottom: 26 }}>
          <h1 className="h1" style={{ fontSize: 26 }}>Şifremi unuttum</h1>
          <p className="dim" style={{ marginTop: 8, fontSize: 13.5, lineHeight: 1.6 }}>
            Kayıtlı e-posta adresinizi girin, sıfırlama bağlantısı gönderelim.
          </p>
        </div>

        {sent ? (
          <div className="fade-up">
            {/* Adresin kayıtlı olup olmadığı söylenmiyor: aksi halde bu ekran
                kimlerin üye olduğunu öğrenmek için kullanılabilirdi. */}
            <div className="card" style={{ textAlign: 'center', padding: '22px 18px' }}>
              <div style={{ marginBottom: 10 }}><Icon name="shield" size={26} /></div>
              <div style={{ fontWeight: 700, marginBottom: 8 }}>Bağlantı gönderildi</div>
              <div className="dim" style={{ fontSize: 13, lineHeight: 1.6 }}>
                Adres kayıtlıysa sıfırlama bağlantısı e-postanıza ulaşacak.
                Bağlantı 1 saat geçerli ve yalnızca bir kez kullanılabilir.
              </div>
            </div>

            <div className="card" style={{ marginTop: 14, fontSize: 12, lineHeight: 1.6 }}>
              Şifrenizi sıfırladığınızda yeni bir şifreleme anahtarı oluşturulur;
              <b> sıfırlama öncesi mesajlarınız okunamaz hale gelir.</b>
            </div>
          </div>
        ) : (
          <form onSubmit={submit} className="fade-up" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <input className="input" type="email" placeholder="E-posta adresi" value={email}
              onChange={(e) => setEmail(e.target.value)} required autoComplete="username" />

            {error && <div className="pill rose" style={{ justifyContent: 'center' }}>{error}</div>}

            <button className="btn primary block" type="submit" disabled={loading} style={{ height: 52 }}>
              {loading ? <div className="spinner" /> : 'Sıfırlama Bağlantısı Gönder'}
            </button>
          </form>
        )}

        <div style={{ textAlign: 'center', marginTop: 20, fontSize: 13 }} className="dim">
          <Link to="/" style={{ color: 'var(--brand)', fontWeight: 600 }}>Girişe dön</Link>
        </div>
      </div>
    </div>
  )
}
