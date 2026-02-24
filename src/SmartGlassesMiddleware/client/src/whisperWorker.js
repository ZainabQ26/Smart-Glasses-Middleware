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
let pipelineFn = null;

async function ensureTransformers() {
  if (pipelineFn) return;

  const { pipeline, env } = await import('@huggingface/transformers');
  pipelineFn = pipeline;

  env.allowLocalModels = false;
  env.useBrowserCache = true;

  // Choose ONNX WASM thread count dynamically (cap at 8).
  // Threads require SharedArrayBuffer/WebAssembly threads (and COOP+COEP when
  // running in the browser). Mobile browsers and webviews may not support
  // this — when unavailable we fall back to 1 thread for maximum compatibility.
  let desiredThreads = 1;
  try {
    const hw = (typeof navigator !== 'undefined' && navigator.hardwareConcurrency) ? navigator.hardwareConcurrency : 2;
    if (typeof SharedArrayBuffer !== 'undefined' && typeof MessageChannel !== 'undefined') {
      try {
        // Quick runtime probe: try posting a tiny SharedArrayBuffer to ensure
        // SharedArrayBuffer is usable in this context (will throw if blocked).
        const mc = new MessageChannel();
        mc.port1.postMessage(new SharedArrayBuffer(1));

        // Use a conservative fraction of logical cores and cap at 8.
        // For phones, half the cores is a safe default (e.g. 2 on 4-core devices).
        desiredThreads = Math.min(8, Math.max(1, Math.floor(hw / 2)));
      } catch (e) {
        desiredThreads = 1;
      }
    }
  } catch (e) {
    desiredThreads = 1;
  }

  env.backends.onnx.wasm.numThreads = desiredThreads;
}

// ── Model ----------------------------------------------------------------
// whisper-tiny.en — English-only, ~39 MB, fast on mid-range hardware.
// Swap for 'Xenova/whisper-base.en' (~74 MB) or 'Xenova/whisper-small.en'
// (~244 MB) if you need better accuracy.
const MODEL_ID = 'Xenova/whisper-tiny.en';

let transcriber = null;

async function getTranscriber() {
  if (transcriber) return transcriber;

  await ensureTransformers();

  self.postMessage({ type: 'loading', message: 'Loading Whisper model…' });

  transcriber = await pipelineFn(
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
      // English-only model: avoid task/language generation options that are
      // only valid for multilingual checkpoints.
      do_sample: false,
    });

    self.postMessage({ type: 'result', text: (result.text ?? '').trim() });
  } catch (err) {
    self.postMessage({ type: 'error', message: err?.message ?? String(err) });
  }
};
