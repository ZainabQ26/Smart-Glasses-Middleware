/**
 * Web Worker: runs Whisper (via @huggingface/transformers + ONNX Runtime)
 * entirely in the browser.
 *
 * A single multilingual model (whisper-base) is used for all modes:
 *   - 'wake-word' : fast check for the activation keyword
 *   - 'partial'   : lightweight in-progress transcription shown while the user speaks
 *   - 'command'   : full-quality final transcription sent to the AI
 *
 * Using one model avoids loading overhead and makes Spanish (and other language)
 * support trivial to add later.
 *
 * Message protocol
 * ────────────────
 * Main → Worker:
 *   { type: 'transcribe', audio: Float32Array, mode: 'wake-word'|'partial'|'command', language?: string }
 *   { type: 'preload' }   – eagerly warm the model (optional optimisation)
 *
 * Worker → Main:
 *   { type: 'loading',  message: string }
 *   { type: 'result',   text: string, mode: string }
 *   { type: 'error',    message: string, mode?: string }
 *
 * Runtime selection (priority order):
 *   1. WebGPU — hardware-accelerated (where supported)
 *   2. WASM   — cross-platform software fallback
 */

// ── Global error capture ─────────────────────────────────────────────────────
self.addEventListener('error', (e) => {
  self.postMessage({ type: 'error', message: `Worker error: ${e.message} (${e.filename}:${e.lineno})` });
});
self.addEventListener('unhandledrejection', (e) => {
  self.postMessage({ type: 'error', message: `Worker async error: ${e.reason?.message ?? e.reason}` });
});

// ── Model configuration ──────────────────────────────────────────────────────
// whisper-base multilingual — good balance of speed (~145 MB) and accuracy;
// supports 99 languages including Spanish without any model swap.
const MODEL_ID = 'Xenova/whisper-base';

let pipelineFn     = null;
let resolvedDevice = null;
let transcriber    = null;   // lazily initialised, reused across calls

// ── Environment detection ────────────────────────────────────────────────────

/** Probe whether WebGPU is available and able to create a GPU device. */
async function probeWebGPU() {
  try {
    if (typeof navigator === 'undefined' || !navigator.gpu) return false;
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) return false;
    const device = await adapter.requestDevice();
    try { device.destroy(); } catch (_) {}
    return true;
  } catch (_) {
    return false;
  }
}

async function ensureTransformers() {
  if (pipelineFn) return;

  const { pipeline, env } = await import('@huggingface/transformers');
  pipelineFn = pipeline;

  env.allowLocalModels  = false;
  env.allowRemoteModels = true;
  env.useBrowserCache   = true;

  const gpuAvailable = await probeWebGPU();
  if (gpuAvailable) {
    resolvedDevice = 'webgpu';
    self.postMessage({ type: 'loading', message: 'Using WebGPU backend for Whisper' });
  } else {
    resolvedDevice = 'wasm';
    let desiredThreads = 1;
    try {
      const hw = (typeof navigator !== 'undefined' && navigator.hardwareConcurrency) ? navigator.hardwareConcurrency : 2;
      if (typeof SharedArrayBuffer !== 'undefined' && typeof MessageChannel !== 'undefined') {
        try {
          const mc = new MessageChannel();
          mc.port1.postMessage(new SharedArrayBuffer(1));
          desiredThreads = Math.min(8, Math.max(1, Math.floor(hw / 2)));
        } catch (_) {
          desiredThreads = 1;
        }
      }
    } catch (_) {
      desiredThreads = 1;
    }
    env.backends.onnx.wasm.numThreads = desiredThreads;
    self.postMessage({ type: 'loading', message: `Using WASM backend for Whisper (${desiredThreads} thread${desiredThreads !== 1 ? 's' : ''})` });
  }
}

// ── Model loader (singleton) ─────────────────────────────────────────────────

async function getTranscriber() {
  if (transcriber) return transcriber;

  await ensureTransformers();

  self.postMessage({ type: 'loading', message: 'Loading Whisper model…' });

  const opts = {
    progress_callback: (p) => {
      if (p.status === 'downloading') {
        const pct = Math.round(p.progress ?? 0);
        self.postMessage({ type: 'loading', message: `Downloading Whisper model… ${pct}%` });
      }
      if (p.status === 'ready') {
        // Signal to the main thread that the model is ready.
        // Send only the explicit 'ready' message (avoid duplicate 'loading').
        self.postMessage({ type: 'ready', message: `Whisper model ready (${resolvedDevice})` });
      }
    },
    dtype: resolvedDevice === 'wasm' ? {
      encoder_model: 'fp32',
      decoder_model_merged: 'q4'
    } : undefined
  };

  if (resolvedDevice === 'webgpu') opts.device = 'webgpu';

  transcriber = await pipelineFn('automatic-speech-recognition', MODEL_ID, opts);
  return transcriber;
}

// ── Message handler ──────────────────────────────────────────────────────────

self.onmessage = async (event) => {
  const { type, audio, mode, language } = event.data;

  if (type === 'preload') {
    // Warm the model in the background so the first real request is instant.
    try { await getTranscriber(); } catch (_) {}
    return;
  }

  if (type !== 'transcribe') return;

  try {
    const model = await getTranscriber();

    // Build inference options by mode.
    // - wake-word/partial: prioritize low latency.
    // - command: prioritize accuracy for the final request to the AI.
    const inferOpts =
      mode === 'command'
        ? {
            do_sample: false,
            num_beams: 4,
            // Constrain to the requested language when provided (e.g. 'en', 'es').
            ...(language ? { language } : {}),
          }
        : {
            do_sample: false,
            num_beams: 1,
            // Constrain to the requested language when provided (e.g. 'en', 'es').
            ...(language ? { language } : {}),
          };

    const result = await model(audio, inferOpts);
    const text   = (result.text ?? '').trim();

    self.postMessage({ type: 'result', text, mode: mode ?? 'wake-word' });
  } catch (err) {
    self.postMessage({ type: 'error', message: err?.message ?? String(err), mode: mode ?? 'wake-word' });
  }
};

