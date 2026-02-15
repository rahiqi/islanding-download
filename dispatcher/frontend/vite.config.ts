import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: 'http://localhost:5062', changeOrigin: true },
      '/openapi': { target: 'http://localhost:5062', changeOrigin: true },
    },
  },
  build: {
    outDir: '../dispatcher/wwwroot',
    emptyOutDir: true,
  },
})
