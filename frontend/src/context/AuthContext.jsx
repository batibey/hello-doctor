import { createContext, useContext, useEffect, useState } from 'react'
import api from '../api/client'
import { authVerifier, createKeyBundle, unwrapPrivateKey, cryptoAvailable } from '../crypto/keys'
import { savePrivateKey, loadPrivateKey, clearPrivateKey } from '../crypto/keyStore'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [user, setUser] = useState(() => {
    const raw = localStorage.getItem('hd_user')
    return raw ? JSON.parse(raw) : null
  })
  // Oturum doğrulanana kadar ekran çizilmez; aksi halde bayat token'la açılışta
  // uygulama görünüp hemen giriş ekranına düşüyordu.
  const [ready, setReady] = useState(false)
  const [privateKey, setPrivateKey] = useState(null)

  // Girişten sonra anahtar çiftini hazırlar. Hesapta anahtar yoksa (tohumlanan
  // demo hesaplar ve şifrelemeden önce açılmış hesaplar) parola hâlâ elimizdeyken
  // burada üretilip yüklenir — sunucu ham parolayı bilmediği için bunu yapamaz.
  const setupKeys = async (userId, password, keys) => {
    if (!cryptoAvailable()) return null

    if (keys?.wrappedPrivateKey) {
      const key = await unwrapPrivateKey(password, keys)
      await savePrivateKey(userId, key)
      return key
    }

    const { bundle, privateKey: key } = await createKeyBundle(password)
    await api.post('/users/keys', bundle)
    await savePrivateKey(userId, key)
    return key
  }

  const finishAuth = async (data, password) => {
    localStorage.setItem('hd_token', data.token)
    localStorage.setItem('hd_user', JSON.stringify(data.user))
    const key = await setupKeys(data.user.id, password, data.keys)
    setPrivateKey(key)
    setUser(data.user)
    return data.user
  }

  const login = async (email, password, role) => {
    const { data } = await api.post('/auth/login', {
      email: email.trim(),
      password: await authVerifier(password),
      role,
    })
    return finishAuth(data, password)
  }

  const register = async (form) => {
    const { bundle } = await createKeyBundle(form.password)
    const { data } = await api.post('/auth/register', {
      ...form,
      email: form.email.trim(),
      password: await authVerifier(form.password),
      ...bundle,
    })
    return finishAuth(data, form.password)
  }

  const forgotPassword = (email) =>
    api.post('/auth/forgot-password', { email: email.trim() }).then((r) => r.data)

  // Eski parola bilinmediği için eski özel anahtar açılamaz; yeni bir çift
  // üretiliyor ve sıfırlama öncesi mesajlar okunamaz hale geliyor.
  const resetPassword = async (token, password) => {
    const { bundle } = await createKeyBundle(password)
    const { data } = await api.post('/auth/reset-password', {
      token,
      password: await authVerifier(password),
      ...bundle,
    })
    return finishAuth(data, password)
  }

  const logout = () => {
    const id = user?.id
    localStorage.removeItem('hd_token')
    localStorage.removeItem('hd_user')
    if (id) clearPrivateKey(id)
    setPrivateKey(null)
    setUser(null)
  }

  // Açılışta token'ı doğrula ve cihazdaki özel anahtarı geri yükle.
  useEffect(() => {
    let cancelled = false
    const stored = localStorage.getItem('hd_user')

    if (!stored || !localStorage.getItem('hd_token')) {
      setReady(true)
      return
    }

    const run = async () => {
      try {
        const { data } = await api.get('/users/me')
        if (cancelled) return
        localStorage.setItem('hd_user', JSON.stringify(data))
        setUser(data)
        setPrivateKey(await loadPrivateKey(data.id))
      } catch {
        if (!cancelled) logout()
      } finally {
        if (!cancelled) setReady(true)
      }
    }
    run()

    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <AuthContext.Provider value={{
      user, ready, privateKey, login, register, forgotPassword, resetPassword, logout,
      isDoctor: user?.role === 'Doctor',
    }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)
