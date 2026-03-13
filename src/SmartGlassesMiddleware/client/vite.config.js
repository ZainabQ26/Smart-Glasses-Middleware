import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
// vite-plugin-static-copy removed; no static VAD/Whisper assets to copy

export default defineConfig({
  plugins: [
    react(),
    // VAD/Whisper assets no longer copied; VAD removed from client.
  ],
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
  // Bundle Web Workers as ES modules so `import` statements inside the worker
  // (e.g. @xenova/transformers) are handled correctly by Rollup.
  worker: {
    format: 'es',
  },
  base: '/',
})
