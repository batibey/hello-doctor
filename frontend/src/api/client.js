import axios from 'axios'

// Uses Vite proxy (/api -> http://localhost:5088) so it works on LAN/mobile too.
const api = axios.create({ baseURL: '/api' })

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('hd_token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

export default api
