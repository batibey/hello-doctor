import { useEffect, useState } from 'react'
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom'
import { AuthProvider, useAuth } from './context/AuthContext'
import { RealtimeProvider, useRealtime } from './context/RealtimeContext'
import api from './api/client'
import BottomNav from './components/BottomNav'
import CallOverlay from './components/CallOverlay'
import LoginScreen from './screens/LoginScreen'
import HomeScreen from './screens/HomeScreen'
import AppointmentsScreen from './screens/AppointmentsScreen'
import ConversationsScreen from './screens/ConversationsScreen'
import ChatScreen from './screens/ChatScreen'
import ProfileScreen from './screens/ProfileScreen'
import DoctorProfileScreen from './screens/DoctorProfileScreen'

function Shell() {
  const { pathname } = useLocation()
  const { onMessage } = useRealtime()
  const [unread, setUnread] = useState(0)

  const loadUnread = () => api.get('/messages/conversations')
    .then(({ data }) => setUnread(data.reduce((s, c) => s + c.unread, 0)))
    .catch(() => {})

  useEffect(() => { loadUnread() }, [pathname])
  useEffect(() => onMessage(() => loadUnread()), [onMessage])

  // Hide bottom nav on chat / booking / call screens
  const hideNav = pathname.startsWith('/chat/') || pathname.startsWith('/doctor/')

  return (
    <div className="screen">
      <Routes>
        <Route path="/" element={<HomeScreen />} />
        <Route path="/appointments" element={<AppointmentsScreen />} />
        <Route path="/messages" element={<ConversationsScreen />} />
        <Route path="/chat/:userId" element={<ChatScreen />} />
        <Route path="/doctor/:id" element={<DoctorProfileScreen />} />
        <Route path="/profile" element={<ProfileScreen />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      {!hideNav && <BottomNav unread={unread} />}
      <CallOverlay />
    </div>
  )
}

function Gate() {
  const { user } = useAuth()
  if (!user) return <LoginScreen />
  return (
    <RealtimeProvider>
      <Shell />
    </RealtimeProvider>
  )
}

export default function App() {
  return (
    <div className="device-stage">
      <div className="phone">
        <BrowserRouter>
          <AuthProvider>
            <Gate />
          </AuthProvider>
        </BrowserRouter>
      </div>
    </div>
  )
}
