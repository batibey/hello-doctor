import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5088', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5088', changeOrigin: true, ws: true },
    },
  },
})
