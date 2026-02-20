import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Aspire injects service URLs as env vars (e.g., services__lucia-agenthost__https__0)
const apiTarget =
  process.env['services__lucia-agenthost__https__0'] ??
  process.env['services__lucia-agenthost__http__0'] ??
  'http://localhost:5151'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  server: {
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
      '/agents': {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
      '/agent': {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
      '/a2a': {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
