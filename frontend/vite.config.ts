import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import tailwindcss from '@tailwindcss/vite'
import istanbul from 'vite-plugin-istanbul'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    tailwindcss(),
    vue(),
    vueDevTools(),
    // Coverage instrumentation — only active when VITE_COVERAGE=true.
    // The plugin injects Istanbul counters into every .ts/.vue file under src/,
    // populating window.__coverage__ during e2e runs.  Normal dev/build
    // builds are unaffected when the env var is absent.
    istanbul({
      include: 'src/**',
      exclude: ['node_modules', 'src/**/*.spec.*', 'src/**/*.test.*'],
      extension: ['.js', '.ts', '.vue'],
      requireEnv: true, // only instruments when VITE_COVERAGE=true
    }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  server: {
    port: 5173,
    host: '0.0.0.0',
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})
