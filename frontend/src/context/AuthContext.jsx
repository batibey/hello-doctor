import { createContext, useContext, useEffect, useState } from 'react'
import api from '../api/client'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [user, setUser] = useState(() => {
    const raw = localStorage.getItem('hd_user')
    return raw ? JSON.parse(raw) : null
  })
  const [ready, setReady] = useState(true)

  const login = async (email, password, role) => {
    const { data } = await api.post('/auth/login', { email, password, role })
    localStorage.setItem('hd_token', data.token)
    localStorage.setItem('hd_user', JSON.stringify(data.user))
    setUser(data.user)
    return data.user
  }

  const logout = () => {
    localStorage.removeItem('hd_token')
    localStorage.removeItem('hd_user')
    setUser(null)
  }

  // Keep user fresh if token still valid
  useEffect(() => {
    if (!user) return
    api.get('/users/me')
      .then(({ data }) => {
        localStorage.setItem('hd_user', JSON.stringify(data))
        setUser(data)
      })
      .catch(() => logout())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <AuthContext.Provider value={{ user, ready, login, logout, isDoctor: user?.role === 'Doctor' }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)
