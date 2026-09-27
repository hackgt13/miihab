import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // Served by the RehabMii coordinator at /ehr/ (coordinator/server.ts), same origin as the data it reads.
  base: '/ehr/',
  // The RehabMii coordinator. Proxied rather than called cross-origin: it sends no CORS headers.
  server: { proxy: { '/api': 'http://127.0.0.1:8766', '/exercise': { target: 'ws://127.0.0.1:8766', ws: true } } },
})
