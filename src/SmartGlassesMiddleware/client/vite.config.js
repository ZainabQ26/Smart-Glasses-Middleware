import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteStaticCopy } from 'vite-plugin-static-copy'

export default defineConfig({
  plugins: [
    react(),
    // We are now loading VAD files from the CDN by removing baseAssetPath="/" in App.jsx
    // So we don't strictly need to copy them locally anymore, but keeping them as backup is fine.
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
        {
          // .mjs companion files — ONNX Runtime loads these dynamically as ES modules
          src: 'node_modules/onnxruntime-web/dist/ort-wasm-simd-threaded*.mjs',
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
  // Bundle Web Workers as ES modules so `import` statements inside the worker
  // (e.g. @xenova/transformers) are handled correctly by Rollup.
  worker: {
    format: 'es',
  },
  base: '/',
})
