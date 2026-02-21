import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteStaticCopy } from 'vite-plugin-static-copy'

export default defineConfig({
  plugins: [
    react(),
    // Copy the Silero VAD model and ONNX Runtime WASM files into the build output
    // so they're served as static assets at /silero_vad.onnx, /vad.worklet.bundle.min.js, etc.
    viteStaticCopy({
      targets: [
        {
          src: 'node_modules/@ricky0123/vad-web/dist/silero_vad_legacy.onnx',
          dest: './',
        },
        {
          src: 'node_modules/@ricky0123/vad-web/dist/silero_vad_v5.onnx',
          dest: './',
        },
        {
          src: 'node_modules/@ricky0123/vad-web/dist/vad.worklet.bundle.min.js',
          dest: './',
        },
        {
          src: 'node_modules/onnxruntime-web/dist/*.wasm',
          dest: './',
        },
      ],
    }),
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
  base: '/',
})
