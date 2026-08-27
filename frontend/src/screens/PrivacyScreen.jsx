import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import api from '../api/client'
import { useAuth } from '../context/AuthContext'
import { Icon, Loader } from '../components/ui'

// KVKK veri sahibi hakları: verilerine erişme, kimlerin eriştiğini görme,
// rızayı geri alma ve hesabı silme.
export default function PrivacyScreen() {
  const nav = useNavigate()
  const { logout } = useAuth()
  const [log, setLog] = useState(null)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [confirmText, setConfirmText] = useState('')
  const [showDelete, setShowDelete] = useState(false)

  useEffect(() => {
    api.get('/admin/access-log?take=50')
      .then(({ data }) => setLog(data))
      .catch(() => setLog([]))
  }, [])

  // Tarayıcı sanal alanı indirmeyi engelleyebildiği için dosyayı bir Blob
  // üzerinden veriyoruz; sunucu yanıtı zaten JSON.
  const exportData = async () => {
    setBusy('export'); setError('')
    try {
      const { data } = await api.get('/privacy/export')
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `hellodoctor-verilerim-${new Date().toISOString().slice(0, 10)}.json`
      a.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('Veriler dışa aktarılamadı.')
    } finally {
      setBusy('')
    }
  }

  const deleteAccount = async () => {
    setBusy('delete'); setError('')
    try {
      await api.post('/privacy/delete-account', { confirmation: confirmText })
      logout()
      nav('/', { replace: true })
    } catch (err) {
      setError(err?.response?.data?.message || 'Hesap silinemedi.')
    } finally {
      setBusy('')
    }
  }

  const actionLabel = {
    'conversation.read': 'Sohbetinizi açtı',
    'profile.read': 'Profilinizi görüntüledi',
    'appointment.status': 'Randevu durumunu değiştirdi',
    'doctor.verify': 'Doğrulama işlemi yaptı',
    'data.export': 'Verilerinizi dışa aktardınız',
    'account.delete': 'Hesap silme işlemi',
  }

  return (
    <div className="screen">
      <div className="screen-scroll">
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 18 }}>
          <button onClick={() => nav('/profile')} style={{ padding: 4 }}><Icon name="back" size={22} /></button>
          <div className="h2" style={{ margin: 0 }}>Verilerim</div>
        </div>

        {error && <div className="pill rose" style={{ justifyContent: 'center', width: '100%', marginBottom: 14 }}>{error}</div>}

        <div className="section-title" style={{ marginBottom: 10 }}>Haklarınız</div>
        <div className="stack" style={{ gap: 10, marginBottom: 24 }}>
          <button className="btn block" onClick={exportData} disabled={busy === 'export'}
            style={{ justifyContent: 'flex-start', gap: 12, padding: '14px 16px', textAlign: 'left' }}>
            <Icon name="shield" size={18} />
            <span style={{ flex: 1 }}>
              <b>Verilerimi indir</b>
              <div className="dim" style={{ fontSize: 12, marginTop: 2 }}>
                Hesabınız, mesajlarınız, randevularınız ve rıza kayıtlarınız
              </div>
            </span>
            {busy === 'export' && <div className="spinner" />}
          </button>

          <button className="btn block" onClick={() => setShowDelete((v) => !v)}
            style={{ justifyContent: 'flex-start', gap: 12, padding: '14px 16px', textAlign: 'left' }}>
            <Icon name="phone" size={18} style={{ transform: 'rotate(135deg)' }} />
            <span style={{ flex: 1 }}>
              <b>Hesabımı sil</b>
              <div className="dim" style={{ fontSize: 12, marginTop: 2 }}>
                Şifreleme anahtarınız yok edilir, mesajlarınız okunamaz hale gelir
              </div>
            </span>
          </button>
        </div>

        {showDelete && (
          <div className="card" style={{ marginBottom: 24, borderColor: 'rgba(251,113,133,.3)' }}>
            <div style={{ fontSize: 13, lineHeight: 1.6, marginBottom: 12 }}>
              Bu işlem geri alınamaz. Şifreleme anahtarınız yok edileceği için
              mesaj içerikleriniz <b>hiç kimse tarafından</b> okunamaz hale gelir.
              Bekleyen randevularınız iptal edilir.
            </div>
            <input className="input" placeholder="Onaylamak için: HESABIMI SİL"
              value={confirmText} onChange={(e) => setConfirmText(e.target.value)} />
            <button className="btn block" onClick={deleteAccount}
              disabled={confirmText !== 'HESABIMI SİL' || busy === 'delete'}
              style={{ marginTop: 10, background: 'var(--grad-rose)', color: '#fff', height: 46 }}>
              {busy === 'delete' ? <div className="spinner" /> : 'Hesabımı kalıcı olarak sil'}
            </button>
          </div>
        )}

        <div className="section-title" style={{ marginBottom: 10 }}>Verilerinize kimler erişti</div>
        {log === null ? <Loader /> : log.length === 0 ? (
          <div className="card" style={{ textAlign: 'center', padding: 24, color: 'var(--text-dim)', fontSize: 13.5 }}>
            Henüz kimse verilerinize erişmedi.
          </div>
        ) : (
          <div className="stack" style={{ gap: 6 }}>
            {log.map((e, i) => (
              <div key={i} className="card" style={{ padding: '11px 14px', fontSize: 13 }}>
                <div style={{ fontWeight: 600 }}>{actionLabel[e.action] || e.action}</div>
                <div className="faint" style={{ fontSize: 11.5, marginTop: 3 }}>
                  {new Date(e.at).toLocaleString('tr-TR')}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
