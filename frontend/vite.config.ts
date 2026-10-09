import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  // Matches the API's HTTP port in Properties/launchSettings.json. Serving the
  // dev loop over http keeps auth cookies non-Secure so the Vite app can use
  // them. Override with VITE_API_PROXY_TARGET if your local port differs.
  const apiTarget = env.VITE_API_PROXY_TARGET || 'https://ftf.staging.drivesolution.cloud'|| 'http://localhost:64446'

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
        },
      },
    },
  }
})
