/**
 * Web Worker: runs Whisper (via @huggingface/transformers + ONNX Runtime)
 * entirely in the browser.  The main thread sends:
 *   { type: 'transcribe', audio: Float32Array }   // 16 kHz PCM
 *
 * The worker replies with one or more of:
 *   { type: 'loading',  message: string }          // model download progress
 *   { type: 'result',   text: string }             // final transcript
 *   { type: 'error',    message: string }          // something went wrong
 *
 * Runtime selection (in priority order):
 *   1. WebGPU — hardware-accelerated, zero WASM overhead (where supported)
 *   2. WASM   — cross-platform software fallback
 */
// ── Global error capture ────────────────────────────────────────────────
self.addEventListener('error', (e) => {
  self.postMessage({ type: 'error', message: `Worker error: ${e.message} (${e.filename}:${e.lineno})` });
});
self.addEventListener('unhandledrejection', (e) => {
  self.postMessage({ type: 'error', message: `Worker async error: ${e.reason?.message ?? e.reason}` });
});

// ── Environment ---------------------------------------------------------
let pipelineFn = null;
let resolvedDevice = null; // 'webgpu' | 'wasm'

/** Probe whether WebGPU is available and able to create a GPU device. */
async function probeWebGPU() {
  try {
    if (typeof navigator === 'undefined' || !navigator.gpu) return false;
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) return false;
    const device = await adapter.requestDevice();
    device.destroy();
    return true;
  } catch (_) {
    return false;
  }
}

async function ensureTransformers() {
  if (pipelineFn) return;

  const { pipeline, env } = await import('@huggingface/transformers');
  pipelineFn = pipeline;

  env.allowLocalModels = false;
  env.allowRemoteModels = true;
  env.useBrowserCache = true;

  // ── Device selection ────────────────────────────────────────────────
  const gpuAvailable = await probeWebGPU();
  if (gpuAvailable) {
    resolvedDevice = 'webgpu';
    self.postMessage({ type: 'loading', message: 'Using WebGPU backend for Whisper' });
  } else {
    resolvedDevice = 'wasm';
    // Choose ONNX WASM thread count dynamically (cap at 8).
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

// ── Model ----------------------------------------------------------------
// whisper-tiny.en — English-only model loaded remotely from Hugging Face.
const MODEL_ID = 'Xenova/whisper-tiny.en';

let transcriber = null;

async function getTranscriber() {
  if (transcriber) return transcriber;

  await ensureTransformers();

  self.postMessage({ type: 'loading', message: 'Loading Whisper model…' });

  const pipelineOptions = {
    progress_callback: (p) => {
      if (p.status === 'downloading') {
        const pct = Math.round(p.progress ?? 0);
        self.postMessage({
          type: 'loading',
          message: `Downloading Whisper model… ${pct}%`,
        });
      }
      if (p.status === 'ready') {
        self.postMessage({ type: 'loading', message: `Whisper model ready (${resolvedDevice})` });
      }
    },
  };

  // Pass device to the pipeline when WebGPU is available.
  if (resolvedDevice === 'webgpu') {
    pipelineOptions.device = 'webgpu';
  }

  transcriber = await pipelineFn(
    'automatic-speech-recognition',
    MODEL_ID,
    pipelineOptions,
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

    // `audio` is a Float32Array of 16 kHz mono PCM — exactly what Whisper expects.
    const result = await model(audio, {
      do_sample: false,
    });

    self.postMessage({ type: 'result', text: (result.text ?? '').trim() });
    // Release the pipeline after each transcription so that iOS Safari can
    // reclaim the ONNX/WASM memory before the mic is restarted.
    transcriber = null;
  } catch (err) {
    self.postMessage({ type: 'error', message: err?.message ?? String(err) });
  }
};
