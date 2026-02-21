import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    // Output directly into wwwroot so ASP.NET Core serves the built SPA as static files.
    // emptyOutDir: false preserves admin.html, monitoring.html, etc.
    outDir: '../wwwroot',
    emptyOutDir: false,
    rollupOptions: {
      output: {
        // Keep asset names predictable to avoid stale cache issues in CI
        entryFileNames: 'assets/[name]-[hash].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash][extname]',
      },
    },
  },
  base: '/',
})
