import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: 5173,
    // Vite bilinmeyen Host başlıklarını reddeder. Telefondan kamera/mikrofon
    // testi HTTPS gerektirdiği için geçici Cloudflare tüneline izin veriliyor.
    allowedHosts: ['.trycloudflare.com'],
    proxy: {
      '/api': { target: 'http://localhost:5088', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5088', changeOrigin: true, ws: true },
    },
  },
})
