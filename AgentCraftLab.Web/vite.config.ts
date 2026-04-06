import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/copilotkit': {
        target: 'http://localhost:4000',
        changeOrigin: true,
      },
      '/api': {
        target: 'http://localhost:5200',
        changeOrigin: true,
      },
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
})
