import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // The RehabMii coordinator. Proxied rather than called cross-origin: it sends no CORS headers.
  server: { proxy: { '/api': 'http://127.0.0.1:8766' } },
})
