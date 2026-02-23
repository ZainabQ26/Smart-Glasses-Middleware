/**
 * Web Worker: runs Whisper (via @xenova/transformers + ONNX Runtime) entirely
 * in the browser.  The main thread sends:
 *   { type: 'transcribe', audio: Float32Array }   // 16 kHz PCM from VAD
 *
 * The worker replies with one or more of:
 *   { type: 'loading',  message: string }          // model download progress
 *   { type: 'result',   text: string }             // final transcript
 *   { type: 'error',    message: string }          // something went wrong
 */
import { pipeline, env } from '@xenova/transformers';

// ── Global error capture ────────────────────────────────────────────────
// Catch WASM init failures and unhandled promise rejections that occur before
// the message handler is reached (e.g. onnxruntime SIMD crash on first load).
self.addEventListener('error', (e) => {
  self.postMessage({ type: 'error', message: `Worker error: ${e.message} (${e.filename}:${e.lineno})` });
});
self.addEventListener('unhandledrejection', (e) => {
  self.postMessage({ type: 'error', message: `Worker async error: ${e.reason?.message ?? e.reason}` });
});

// ── Environment ---------------------------------------------------------
// Disable local-model lookup; pull from the HuggingFace Hub CDN and cache
// in the browser's Cache Storage (persists across page reloads).
env.allowLocalModels  = false;
env.useBrowserCache   = true;
// Force single-threaded ONNX WASM. When COEP is absent (as in our CloudFront
// config), SharedArrayBuffer is unavailable and ONNX already falls back to the
// single-threaded binary automatically. Setting numThreads=1 here is a
// belt-and-suspenders guard in case headers change in future.
env.backends.onnx.wasm.numThreads = 1;

// ── Model ----------------------------------------------------------------
// whisper-tiny.en — English-only, ~39 MB, fast on mid-range hardware.
// Swap for 'Xenova/whisper-base.en' (~74 MB) or 'Xenova/whisper-small.en'
// (~244 MB) if you need better accuracy.
const MODEL_ID = 'Xenova/whisper-tiny.en';

let transcriber = null;

async function getTranscriber() {
  if (transcriber) return transcriber;

  self.postMessage({ type: 'loading', message: 'Loading Whisper model…' });

  transcriber = await pipeline(
    'automatic-speech-recognition',
    MODEL_ID,
    {
      progress_callback: (p) => {
        if (p.status === 'downloading') {
          const pct = Math.round(p.progress ?? 0);
          self.postMessage({
            type: 'loading',
            message: `Downloading Whisper model… ${pct}%`,
          });
        }
        if (p.status === 'ready') {
          self.postMessage({ type: 'loading', message: 'Whisper model ready' });
        }
      },
    }
  );

  return transcriber;
}

// ── Message handler ------------------------------------------------------
self.onmessage = async (event) => {
  const { type, audio } = event.data;
  if (type !== 'transcribe') return;

  try {
    const model = await getTranscriber();

    self.postMessage({ type: 'loading', message: 'Transcribing…' });

    // `audio` is a Float32Array of 16 kHz mono PCM — exactly what Whisper
    // expects, so no WAV encoding / decoding is needed.
    const result = await model(audio, {
      task: 'transcribe',
      // Disable temperature sampling for deterministic output
      do_sample: false,
    });

    self.postMessage({ type: 'result', text: (result.text ?? '').trim() });
  } catch (err) {
    self.postMessage({ type: 'error', message: err?.message ?? String(err) });
  }
};
