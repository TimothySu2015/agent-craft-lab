import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // CopilotKit Runtime（Node.js，port 4000）
      '/copilotkit': {
        target: 'http://localhost:4000',
        changeOrigin: true,
      },
      // .NET AG-UI 端點（直連，供除錯用）
      '/ag-ui': {
        target: 'http://localhost:5200',
        changeOrigin: true,
      },
      '/info': {
        target: 'http://localhost:5200',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
})
