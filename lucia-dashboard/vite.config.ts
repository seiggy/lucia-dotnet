import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Aspire injects service URLs as env vars (e.g., services__lucia-agenthost__https__0)
const apiTarget =
  process.env['services__lucia-agenthost__https__0'] ??
  process.env['services__lucia-agenthost__http__0'] ??
  'http://localhost:5151'

const proxyOpts = { target: apiTarget, changeOrigin: true, secure: false } as const

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  server: {
    proxy: {
      // API routes — prefix-matched
      '/api': proxyOpts,
      // A2A agent list endpoint (exact path, not prefix — avoids
      // clobbering /agent-dashboard and /agent-definitions SPA routes)
      '/agents': proxyOpts,
      '/a2a': proxyOpts,
      // A2A send-message endpoint — only proxy POST /agent, not SPA routes
      '/agent': {
        ...proxyOpts,
        bypass(req) {
          // Let SPA routes like /agent-dashboard, /agent-definitions through
          if (req.url && req.url.length > '/agent'.length) return req.url
        },
      },
    },
  },
})
