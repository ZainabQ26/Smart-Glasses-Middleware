import React, { useState, useEffect, useRef, useCallback } from "react";
import PdfViewer from "./PdfViewer.jsx";

// ─── MAIN COMPONENT ───────────────────────────────────────────────────────────
//
// Voice pipeline: Groq API (server-side Whisper transcription)
//   - AudioContext + AudioWorklet captures mic at 16 kHz
//   - Amplitude VAD detects speech vs silence
//   - Wake-word mode: each speech chunk sent to server Groq; "Iris" triggers command mode
//   - Command mode: chunks sent as partial results; sentence-end pattern or 2.5 s
//     silence submits the full accumulated audio for final transcription via
//     Groq Whisper Turbo on the server
//
import glassesImg from "./pdf/image/Gemini_Generated_Image_8mwak58mwak58mwa.png";
import pennStateEmblemImg from "./pdf/image/Penn-State-University-Emblem.png";

// ─── PENN STATE SHIELD SVG ────────────────────────────────────────────────────
function PennStateShield({ size = 72 }) {
  const h = Math.round(size * 1.08);
  return (
    <svg
      width={size}
      height={h}
      viewBox="0 0 72 78"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-label="Penn State shield"
      style={{ flexShrink: 0 }}
    >
      {/* Shield body */}
      <path
        d="M36 3 L69 15 L69 47 Q69 67 36 76 Q3 67 3 47 L3 15 Z"
        fill="rgba(255,255,255,0.13)"
        stroke="white"
        strokeWidth="2.5"
        strokeLinejoin="round"
      />
      {/* Mane (outer circle) */}
      <circle cx="36" cy="42" r="18" fill="rgba(255,255,255,0.55)" />
      {/* Head (inner circle) */}
      <circle cx="36" cy="42" r="13" fill="white" />
      {/* Left ear */}
      <polygon points="23,32 17,23 29,29" fill="white" />
      {/* Right ear */}
      <polygon points="49,32 55,23 43,29" fill="white" />
      {/* Eyes */}
      <circle cx="30" cy="39" r="2.4" fill="#1d3461" />
      <circle cx="42" cy="39" r="2.4" fill="#1d3461" />
      {/* Nose */}
      <path d="M34 45 L36 43 L38 45 Z" fill="#1d3461" />
      {/* Mouth */}
      <path d="M31 48 Q36 51 41 48" stroke="#1d3461" strokeWidth="1.4" fill="none" strokeLinecap="round" />
    </svg>
  );
}

// ─── TRANSLATIONS ─────────────────────────────────────────────────────────────
const TRANSLATIONS = {
  en: {
    title: "This is Eye-RIS!\nHow can I help?",
    subtitleTemplate: 'Say "{word}" to start, then speak your command.',
    connectingMic: "Connecting microphone…",
    readyTemplate: 'Ready — say "{word}" to begin',
    readyShort: "Ready",
    howItWorksTitle: "🎤 How It Works",
    steps: [
      'Say {word}',
      "Speak command clearly",
      'Say {word} again for the next command',
    ],
    stepSubs: [
      "(This is to wake the assistant.)",
      "(The assistant will respond or perform the action.)",
      null,
    ],
    commonCommandsTitle: "📋 Common Commands",
    commandsOverlayTitle: "📋 Commands",
    commandList: [
      ["search recent files", "Browse recent uploaded files"],
      ["number #", "Go to file number in recent files list"],
      ["open last document", "Opens last uploaded document"],
      ["next/previous page", "Go to the next/previous page"],
      ["go to page 5", "Jump to a specific page"],
      ["highlight aspirin", "Highlight word in open document"],
      ["clear highlights", "Remove all highlights"],
      ["search for blood pressure", "Search for text"],
      ["search for BP", "Search using FDA SPL abbreviations"],
      ["print document", "Print open document with highlights"],
      ["print highlight", "Print the highlighted pages only"],
    ],
    sendingAI: "🤖 Sending to AI…",
    transcribing: "⏳ Transcribing…",
    listening: "🗣️ Listening…",
    connectingMicShort: "⏳ Connecting mic…",
    sayWordTemplate: 'Say "{word}"',
    you: "You",
    aiThinking: "AI is thinking…",
    aiLabel: "AI",
    advanced: "Advanced",
    recentFiles: "📂 Recent Uploaded Files",
    recentFilesHintTemplate: "Say a number (1–{n}) or tap a file to open it.",
    lastDocument: "📄 Last Uploaded Document",
    previewDocument: "Preview Document →",
    remindersTitle: "📝 Your Reminders",
    noReminders: "No reminders yet.",
    textInputPlaceholderTemplate: 'type "{word}" to start, followed by your command.',
    send: "Send",
    closeLog: "✕ Close Log",
    activityLogBtn: "📋 Activity Log",
    activityLogTitle: "Activity Log",
    noActivityTemplate: 'No activity yet. Say "{word}".',
    noClearCommandTemplate: 'I didn\'t hear a clear command. Please say "{word}" again.',
    commandTooBrief: "That was too brief. Please say a full command.",
    fileSelectionPrompt: "Please say just the file number, like 1, 2, or 3.",
    didntCatch: "I didn't catch that command. Please try again.",
  },
  es: {
    title: "¡Esto es Eye-RIS!\n¿En qué puedo ayudarte?",
    subtitleTemplate: 'Di "{word}" para empezar, luego habla tu comando.',
    connectingMic: "Conectando micrófono…",
    readyTemplate: 'Listo — di "{word}" para comenzar',
    readyShort: "Listo",
    howItWorksTitle: "🎤 Cómo funciona",
    steps: [
      'Di {word}',
      "Pronuncia tu comando claramente",
      'Di {word} de nuevo para el siguiente comando',
    ],
    stepSubs: [
      "(Esto activa el asistente.)",
      "(El asistente responderá o realizará la acción.)",
      null,
    ],
    commonCommandsTitle: "📋 Comandos comunes",
    commandsOverlayTitle: "📋 Comandos",
    commandList: [
      ["buscar archivos recientes", "Explorar archivos subidos recientemente"],
      ["número #", "Ir al número de archivo en la lista reciente"],
      ["abrir último documento", "Abre el último documento subido"],
      ["página siguiente/anterior", "Ir a la página siguiente/anterior"],
      ["ir a la página 5", "Saltar a una página específica"],
      ["resaltar aspirina", "Resaltar palabra en el documento abierto"],
      ["borrar resaltados", "Eliminar todos los resaltados"],
      ["buscar presión arterial", "Buscar texto"],
      ["buscar PA", "Buscar usando abreviaciones FDA SPL"],
      ["imprimir documento", "Imprimir documento con resaltados"],
      ["imprimir resaltado", "Imprimir solo las páginas resaltadas"],
    ],
    sendingAI: "🤖 Enviando a IA…",
    transcribing: "⏳ Transcribiendo…",
    listening: "🗣️ Escuchando…",
    connectingMicShort: "⏳ Conectando mic…",
    sayWordTemplate: 'Di "{word}"',
    you: "Tú",
    aiThinking: "La IA está pensando…",
    aiLabel: "IA",
    advanced: "Avanzado",
    recentFiles: "📂 Archivos subidos recientes",
    recentFilesHintTemplate: "Di un número (1–{n}) o toca un archivo para abrirlo.",
    lastDocument: "📄 Último documento subido",
    previewDocument: "Vista previa →",
    remindersTitle: "📝 Tus recordatorios",
    noReminders: "Aún no hay recordatorios.",
    textInputPlaceholderTemplate: 'escribe "{word}" para empezar, seguido de tu comando.',
    send: "Enviar",
    closeLog: "✕ Cerrar registro",
    activityLogBtn: "📋 Registro de actividad",
    activityLogTitle: "Registro de actividad",
    noActivityTemplate: 'Sin actividad aún. Di "{word}".',
    noClearCommandTemplate: 'No escuché un comando claro. Di "{word}" de nuevo.',
    commandTooBrief: "Eso fue demasiado breve. Por favor di un comando completo.",
    fileSelectionPrompt: "Por favor di solo el número del archivo, como 1, 2 o 3.",
    didntCatch: "No capturé ese comando. Por favor intenta de nuevo.",
  },
};

// Interpolate {key} placeholders in a translation template string.
function tpl(str, vals) {
  return str.replace(/\{(\w+)\}/g, (_, k) => (vals[k] !== undefined ? vals[k] : k));
}

// Render a translation template that contains a single {word} placeholder as
// React nodes, wrapping the word in a highlighted <strong> element.
function renderWithHighlight(template, word) {
  const parts = template.split('{word}');
  if (parts.length < 2) return tpl(template, { word });
  return (
    <>
      {parts[0]}<strong style={{color:"#f97316"}}>"{word}"</strong>{parts[1]}
    </>
  );
}

export default function App({ mode = "voice", language = "en" }) {
  const [status, setStatus]       = useState("idle"); // idle | listening | processing | sending
  const [logs, setLogs]           = useState([]);
  const [inputText, setInputText] = useState("");
  const [transcript, setTranscript] = useState(null);
  const [aiResponse, setAiResponse] = useState(null);
  const [usedAdvancedModel, setUsedAdvancedModel] = useState(false);
  const [wakeWordReady, setWakeWordReady] = useState(false);
  const [pendingAction, _setPendingActionState] = useState(null); // { type: "SHOW_RECENT_FILES", files } | { type: "SELECT_DOCUMENT", id, name } | null
  // Reminders toast: null | { reminders: [], phase: 'visible'|'fading' }
  const [remindersToast, setRemindersToast] = useState(null);
  // Show landing screen until the user says the activation word for the first time.
  const [showLanding, setShowLanding] = useState(true);
  // Activation word is sourced from backend app settings via /api/process/client-settings.
  const [activationWord, setActivationWord] = useState("Iris");
  // Ref copy so the async voice pipeline always reads the current value without
  // needing to restart the AudioContext effect.
  const activationWordRef = useRef(activationWord);
  // Ref copy of the current language so async callbacks always read the latest value.
  const languageRef = useRef(language);
  // Keep languageRef in sync whenever the language prop changes.
  useEffect(() => { languageRef.current = language; }, [language]);
  // Active translation bundle.
  const t = TRANSLATIONS[language] || TRANSLATIONS.en;
  // Wrapper keeps the ref in sync so async callbacks always read the current value.
  const setPendingAction = useCallback((val) => {
    pendingActionRef.current = typeof val === "function" ? val(pendingActionRef.current) : val;
    _setPendingActionState(val);
  }, []);
  const [logOpen, setLogOpen]     = useState(false);

  // Inline PDF viewer state.
  const [openPdf, setOpenPdf] = useState(null); // { id, name } | null
  const pdfViewerRef = useRef(null);
  const handleClosePdf = useCallback(() => setOpenPdf(null), []);

  const activatedRef        = useRef(false);
  const vadRef              = useRef(null);   // VAD removed; kept so optional calls are no-ops
  const listeningTimeoutRef = useRef(null);
  const sendToAIRef         = useRef(null);
  const commandSubmitTimeoutRef = useRef(null); // sentence-end short timer
  // Audio pipeline refs
  const audioCtxRef   = useRef(null);
  const streamRef     = useRef(null);
  const processorRef  = useRef(null);
  // Reminders toast dismiss timers
  const remindersTimerRef     = useRef(null);
  const remindersFadeTimerRef = useRef(null);
  // Tracks the latest pendingAction value without closure-staleness issues.
  // Updated synchronously inside the setPendingAction wrapper defined above.
  const pendingActionRef = useRef(null);

  // Allow up to 60 s for AI responses — matches the backend AIServiceTimeoutSeconds setting.
  const API_TIMEOUT_MS = 60_000;
  const DOCUMENTS_TIMEOUT_MS = 10_000;
  const LISTENING_IDLE_TIMEOUT_MS = 10_000;
  // Bubbles auto-dismiss after this delay (transcript bubble and AI response bubble).
  const BUBBLE_DISMISS_MS = 10_000;
  // How long to keep the mic listening after showing the file list so the user
  // can say a number without repeating the "activate" wake-word.
  const FILE_SELECTION_TIMEOUT_MS = 60_000;

  // Auto-dismiss timers for toast-style bubbles.
  const transcriptDismissRef = useRef(null);
  const aiResponseDismissRef = useRef(null);

  // Helper: show a transcript and schedule auto-dismiss.
  const showTranscript = useCallback((text) => {
    setTranscript(text);
    if (transcriptDismissRef.current) clearTimeout(transcriptDismissRef.current);
    if (text) {
      transcriptDismissRef.current = setTimeout(() => setTranscript(null), BUBBLE_DISMISS_MS);
    }
  }, []);

  // Helper: show an AI response and schedule auto-dismiss.
  const showAiResponse = useCallback((text) => {
    setAiResponse(text);
    if (aiResponseDismissRef.current) clearTimeout(aiResponseDismissRef.current);
    if (text) {
      // Keep the input bubble visible while the response bubble is visible,
      // then clear both together when the response auto-dismisses.
      if (transcriptDismissRef.current) {
        clearTimeout(transcriptDismissRef.current);
        transcriptDismissRef.current = null;
      }
      aiResponseDismissRef.current = setTimeout(() => {
        setAiResponse(null);
        setTranscript(null);
      }, BUBBLE_DISMISS_MS);
    }
  }, []);

  const normalizeCommandText = useCallback((value) => {
    const raw = (value ?? "").trim();
    if (!raw) return "";
    const word = activationWordRef.current;
    const wordEsc = word.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const withoutWakeWord = raw.replace(new RegExp(`^${wordEsc}[\\s,:-]*`, "i"), "").trim();
    return withoutWakeWord || raw;
  }, []);

  const fetchJsonWithTimeout = useCallback(async (url, options = {}, timeoutMs = API_TIMEOUT_MS) => {
    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), timeoutMs);

    try {
      const response = await fetch(url, { ...options, signal: controller.signal });
      let payload = null;

      try {
        payload = await response.json();
      } catch (_) {
        payload = null;
      }

      if (!response.ok) {
        const serverError = payload?.error || payload?.details;
        throw new Error(serverError || `HTTP ${response.status}`);
      }

      return payload;
    } catch (err) {
      if (err?.name === "AbortError") {
        throw new Error("Request timed out");
      }
      throw err;
    } finally {
      window.clearTimeout(timeoutId);
    }
  }, [API_TIMEOUT_MS]);

  const float32ToWavBlob = useCallback((samples, sampleRate = 16000) => {
    const bytesPerSample = 2;
    const blockAlign = bytesPerSample;
    const byteRate = sampleRate * blockAlign;
    const dataSize = samples.length * bytesPerSample;
    const buffer = new ArrayBuffer(44 + dataSize);
    const view = new DataView(buffer);

    const writeString = (offset, str) => {
      for (let i = 0; i < str.length; i += 1) {
        view.setUint8(offset + i, str.charCodeAt(i));
      }
    };

    writeString(0, "RIFF");
    view.setUint32(4, 36 + dataSize, true);
    writeString(8, "WAVE");
    writeString(12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, 1, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, byteRate, true);
    view.setUint16(32, blockAlign, true);
    view.setUint16(34, 16, true);
    writeString(36, "data");
    view.setUint32(40, dataSize, true);

    let offset = 44;
    for (let i = 0; i < samples.length; i += 1) {
      const s = Math.max(-1, Math.min(1, samples[i]));
      const pcm = s < 0 ? s * 0x8000 : s * 0x7fff;
      view.setInt16(offset, pcm, true);
      offset += 2;
    }

    return new Blob([buffer], { type: "audio/wav" });
  }, []);

  const addLog = useCallback((text, type = "info") => {
    setLogs(prev =>
      [{ id: Date.now() + Math.random(), text, type, ts: new Date().toLocaleTimeString() }, ...prev].slice(0, 50)
    );
  }, []);

  // Hydrated from /api/process/client-settings (AI:Groq:ClientMaxRequestsPerMinute).
  const [groqMaxRequestsPerMinute, setGroqMaxRequestsPerMinute] = useState(1);
  const GROQ_MAX_REQUESTS_PER_SECOND = 1;
  const GROQ_MIN_INTERVAL_MS = Math.max(
    Math.ceil(1000 / GROQ_MAX_REQUESTS_PER_SECOND),
    Math.ceil(60_000 / Math.max(1, groqMaxRequestsPerMinute)),
  );
  const groqLimiterRef = useRef({
    windowStartMs: Date.now(),
    count: 0,
    lastRequestAtMs: 0,
    lastThrottleLogAtMs: 0,
  });

  const tryAcquireGroqBudget = useCallback(async ({ canWait = false, maxWaitMs = 0 } = {}) => {
    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
    const limiter = groqLimiterRef.current;
    const now = Date.now();

    if (now - limiter.windowStartMs >= 60_000) {
      limiter.windowStartMs = now;
      limiter.count = 0;
    }

    const waitMs = Math.max(0, GROQ_MIN_INTERVAL_MS - (now - limiter.lastRequestAtMs));
    if (waitMs > 0) {
      if (!canWait || waitMs > maxWaitMs) {
        return false;
      }
      await sleep(waitMs);
    }

    const nowAfterWait = Date.now();
    if (nowAfterWait - limiter.windowStartMs >= 60_000) {
      limiter.windowStartMs = nowAfterWait;
      limiter.count = 0;
    }

    if (limiter.count >= groqMaxRequestsPerMinute) {
      return false;
    }

    limiter.count += 1;
    limiter.lastRequestAtMs = nowAfterWait;
    return true;
  }, [groqMaxRequestsPerMinute, GROQ_MIN_INTERVAL_MS]);

  const logGroqThrottle = useCallback((message) => {
    const limiter = groqLimiterRef.current;
    const now = Date.now();
    if (now - limiter.lastThrottleLogAtMs < 5000) {
      return;
    }
    limiter.lastThrottleLogAtMs = now;
    addLog(message, "warn");
  }, [addLog]);

  const transcribeCommandWithGroq = useCallback(async (audio, { isFinal = false } = {}) => {
    try {
      if (!audio || audio.length === 0) {
        return null;
      }

      const allowed = await tryAcquireGroqBudget({
        canWait: isFinal,
        maxWaitMs: isFinal ? 5000 : 0,
      });
      if (!allowed) {
        if (!isFinal) {
          logGroqThrottle("⏳ Skipping partial transcription to stay under Groq rate limit.");
          return null;
        }

        // Never drop the final command due to local throttling; let the backend/provider decide.
        addLog("⏳ Local throttle active — sending final command transcription anyway.", "warn");
      }

      const audioBlob = float32ToWavBlob(audio);
      const formData = new FormData();
      formData.append("audio", audioBlob, "audio.wav");
      formData.append("language", languageRef.current);
      formData.append("checkWakeword", "false");

      const payload = await fetchJsonWithTimeout(
        "/api/process/transcribe",
        {
          method: "POST",
          body: formData,
        },
        API_TIMEOUT_MS,
      );

      const text = (payload?.text || "").trim();
      const model = (payload?.model || "").trim();
      if (text) {
        addLog(`🧠 Command model: ${model || "unknown"}`, "info");
      }
      console.log("[Voice Debug] Heard command transcription (raw):", text);
      return text || null;
    } catch (err) {
      addLog(`⚠️ Groq transcription error: ${err?.message || "unknown error"}`, "warn");
      return null;
    }
  }, [API_TIMEOUT_MS, addLog, fetchJsonWithTimeout, float32ToWavBlob, tryAcquireGroqBudget, logGroqThrottle]);

  const transcribeWakewordWithGroq = useCallback(async (audio) => {
    try {
      if (!audio || audio.length === 0) {
        return { text: "", matched: false, commandAfterWakeword: "" };
      }

      const allowed = await tryAcquireGroqBudget();
      if (!allowed) {
        logGroqThrottle("⏳ Skipping wake-word check to stay under Groq rate limit.");
        return { text: "", matched: false, commandAfterWakeword: "" };
      }

      const audioBlob = float32ToWavBlob(audio);
      const formData = new FormData();
      formData.append("audio", audioBlob, "audio.wav");
      formData.append("language", languageRef.current);
      formData.append("checkWakeword", "true");

      const payload = await fetchJsonWithTimeout(
        "/api/process/transcribe",
        {
          method: "POST",
          body: formData,
        },
        5000,
      );

      const text = (payload?.text || "").trim();
      const model = (payload?.model || "").trim();
      const matched = payload?.matched ?? text.toLowerCase().includes("Iris");
      const commandAfterWakeword = (payload?.commandAfterWakeword || "").trim();
      const target = (activationWordRef.current || "Iris").trim();
      console.log(`[Voice Debug] Wake-word check: "${text}" (target "${target}") → ${matched ? "MATCH ✅" : "no match"}`);
      if (text) {
        addLog(`👂 Wake-word heard: "${text}" (${model || "unknown"})`, "info");
      } else {
        console.log("[Voice Debug] Wake-word transcription was empty.");
      }
      return { text, matched, commandAfterWakeword };
    } catch (err) {
      addLog(`⚠️ Wake-word transcription failed: ${err?.message || "unknown"}`, "warn");
      return { text: "", matched: false, commandAfterWakeword: "" };
    }
  }, [addLog, fetchJsonWithTimeout, float32ToWavBlob, tryAcquireGroqBudget, logGroqThrottle]);



  useEffect(() => {
    return () => {
      if (transcriptDismissRef.current) clearTimeout(transcriptDismissRef.current);
      if (aiResponseDismissRef.current) clearTimeout(aiResponseDismissRef.current);
    };
  }, []);

  // Keep activationWordRef in sync with the state value.
  useEffect(() => {
    activationWordRef.current = activationWord;
  }, [activationWord]);



  // Probe backend on load and keep retrying while the service scales up from idle.
  // This endpoint is lightweight and also provides the activation word.
  useEffect(() => {
    let cancelled = false;

    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
    const POLL_INTERVAL_MS = 5000;

    const probeBackend = async () => {
      let attempts = 0;

      while (!cancelled) {
        try {
          const response = await fetch("/api/process/client-settings", {
            method: "GET",
            cache: "no-store",
          });

          if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
          }

          const data = await response.json();
          const w = data?.activationWord?.trim() || "";
          if (w) {
            setActivationWord(w);
            activationWordRef.current = w;
          }

          const configuredRpm = Number(data?.groqClientMaxRequestsPerMinute);
          if (Number.isFinite(configuredRpm) && configuredRpm >= 1) {
            setGroqMaxRequestsPerMinute(Math.max(1, Math.floor(configuredRpm)));
          }

          // Groq transcription is handled entirely server-side via /api/process/transcribe-* endpoints

          if (!cancelled) {
            await sleep(POLL_INTERVAL_MS);
          }
        } catch (_) {
          attempts += 1;
          if (!cancelled) {
            await sleep(Math.min(2000 + attempts * 1000, 10000));
          }
        }
      }
    };

    probeBackend();

    return () => {
      cancelled = true;
    };
  }, []);

  // VAD/Whisper removed: keep a harmless placeholder used by the UI so
  // existing button/label logic doesn't need widespread changes.
  const vad = { loading: false, errored: null, userSpeaking: false };

  // ── Reminders toast helper ─────────────────────────────────────────────────
  // Displays a fading toast with the user's saved reminders.
  const REMINDERS_DISPLAY_MS = 9_000;
  const REMINDERS_FADE_MS    = 900;

  const showRemindersToast = useCallback((reminders) => {
    if (remindersTimerRef.current)     clearTimeout(remindersTimerRef.current);
    if (remindersFadeTimerRef.current) clearTimeout(remindersFadeTimerRef.current);

    setRemindersToast({ reminders, phase: "visible" });

    remindersTimerRef.current = setTimeout(() => {
      setRemindersToast(prev => prev ? { ...prev, phase: "fading" } : null);
      remindersFadeTimerRef.current = setTimeout(() => {
        setRemindersToast(null);
      }, REMINDERS_FADE_MS);
    }, REMINDERS_DISPLAY_MS);
  }, []);

  // ── Resume to idle / wake-word mode ───────────────────────────────────────
  // Called after every command (success or error) to return to the listening state.
  const RESUME_DELAY_MS = 300;
  const resumeListening = useCallback(() => {
    if (listeningTimeoutRef.current)      { clearTimeout(listeningTimeoutRef.current);      listeningTimeoutRef.current = null; }
    if (commandSubmitTimeoutRef.current)  { clearTimeout(commandSubmitTimeoutRef.current);  commandSubmitTimeoutRef.current = null; }
    activatedRef.current = false;
    setStatus("idle");
    try { vadRef.current?.pause(); } catch (_) {}
  }, [RESUME_DELAY_MS]);

  const resumeListeningRef = useRef(resumeListening);
  useEffect(() => { resumeListeningRef.current = resumeListening; }, [resumeListening]);

  // Keep a ref copy of `status` so async closures always read the current value.
  const statusRef = useRef(status);
  useEffect(() => { statusRef.current = status; }, [status]);

  const openDocumentPreview = useCallback((document) => {
    if (!document) return;

    const previewId = document.previewId ?? document.managedFileId ?? document.id;
    const name = document.name ?? document.originalFileName ?? document.fileName ?? "Document";

    if (!previewId) {
      addLog("❌ Could not open the selected document.", "error");
      setPendingAction(null);
      showAiResponse("Could not open that document.");
      return;
    }

    setPendingAction(null);
    setOpenPdf({ id: previewId, name });
    addLog(`📄 Opening document: "${name}"`, "info");
  }, [addLog, setPendingAction]);

  const fetchRecentDocuments = useCallback(async () => {
    try {
      let recentFiles = [];

      try {
        const payload = await fetchJsonWithTimeout(
          "/api/documents?page=1&pageSize=5",
          {},
          DOCUMENTS_TIMEOUT_MS,
        );
        const rawDocuments = Array.isArray(payload?.documents)
          ? payload.documents
          : Array.isArray(payload?.Documents)
            ? payload.Documents
            : [];

        recentFiles = rawDocuments
          .map((document) => ({
            id: document.id ?? document.Id ?? null,
            previewId: document.managedFileId ?? document.ManagedFileId ?? document.id ?? document.Id ?? null,
            name: document.originalFileName ?? document.OriginalFileName ?? document.fileName ?? document.FileName ?? "Document",
            uploadedAt: document.uploadedAt ?? document.UploadedAt ?? null,
          }))
          .filter((document) => document.id && document.name)
          .slice(0, 5);
      } catch (docsErr) {
        addLog(`⚠️ Documents list unavailable: ${docsErr?.message || "unknown error"}`, "warn");
      }

      if (recentFiles.length === 0) {
        const adminPayload = await fetchJsonWithTimeout(
          "/api/admin/files?page=1&pageSize=5",
          {},
          DOCUMENTS_TIMEOUT_MS,
        );
        const rawFiles = Array.isArray(adminPayload?.data)
          ? adminPayload.data
          : Array.isArray(adminPayload?.Data)
            ? adminPayload.Data
            : [];

        recentFiles = rawFiles
          .map((file) => ({
            id: file.id ?? file.Id ?? null,
            previewId: file.id ?? file.Id ?? null,
            name: file.label ?? file.Label ?? file.fileName ?? file.FileName ?? "Document",
            uploadedAt: file.uploadedAt ?? file.UploadedAt ?? null,
          }))
          .filter((file) => file.id && file.name)
          .slice(0, 5);

        if (recentFiles.length > 0) {
          addLog("ℹ️ Using admin files list for recent-file actions.", "info");
        }
      }

      if (recentFiles.length === 0) {
        addLog("ℹ️ No uploaded files were found.", "info");
        setPendingAction(null);
        showAiResponse("No uploaded files were found. Please upload a file first.");
        return;
      }

      setAiResponse(null);
      setPendingAction({ type: "SHOW_RECENT_FILES", files: recentFiles });
    } catch (err) {
      addLog(`❌ Recent files error: ${err.message}`, "error");
      setPendingAction(null);
      showAiResponse("I couldn't load the recent files list.");
    }
  }, [addLog, fetchJsonWithTimeout]);

  const summarizeCurrentPage = useCallback(async (targetPageNumber = null) => {
    const localPdf = pdfViewerRef.current;
    if (!localPdf || !openPdf) {
      showAiResponse("No document is open. Open a document first.");
      return;
    }

    const pageData = await localPdf.getCurrentPageText?.(targetPageNumber);
    if (targetPageNumber !== null && typeof targetPageNumber === "number" && !Number.isNaN(targetPageNumber)) {
      localPdf.goToPage?.(targetPageNumber);
    }

    if (!pageData?.text) {
      showAiResponse("I couldn't read the page content.");
      return;
    }

    addLog(`📝 Summarizing page ${pageData.pageNumber} from "${openPdf.name}"`, "voice");
    setPendingAction(null);
    setAiResponse(null);

    const payload = await fetchJsonWithTimeout("/api/process/summarize-page", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        pageText: pageData.text,
        pageNumber: pageData.pageNumber,
        documentName: openPdf.name,
      }),
    }, API_TIMEOUT_MS);

    const summary = (payload?.summary || "").trim();
    if (!summary) {
      showAiResponse("I couldn't summarize the page.");
      return;
    }

    setUsedAdvancedModel(!!payload?.usedAdvancedModel);
    showAiResponse(summary);
  }, [API_TIMEOUT_MS, addLog, fetchJsonWithTimeout, openPdf, showAiResponse, setPendingAction]);

  const buildProcessRequestBody = useCallback(async (commandText) => {
    const payload = { input: commandText };
    const localPdf = pdfViewerRef.current;

    if (!localPdf || !openPdf || !localPdf.getDocumentContext) {
      return payload;
    }

    try {
      const documentContext = await localPdf.getDocumentContext();
      if (documentContext) {
        payload.documentContext = {
          documentName: openPdf.name,
          currentPage: documentContext.currentPage,
          totalPages: documentContext.totalPages,
          pages: documentContext.pages,
          highlights: documentContext.highlights,
        };
      }
    } catch (err) {
      addLog(`⚠️ Failed to collect document context: ${err?.message || "Unknown error"}`, "warn");
    }

    return payload;
  }, [addLog, openPdf]);

  // ── Detect blank audio or background noise ────────────────────────────────
  // Returns true if the transcribed text appears to be blank audio, chatter, or background noise.
  const isBlankAudioOrNoise = useCallback((text) => {
    if (!text || text.length === 0) return true;

    const trimmed = text.trim().toLowerCase();
    
    // Very short fragments are likely noise, unless they are digits (e.g. file numbers)
    if (trimmed.length <= 2 && !/^\d+$/.test(trimmed)) return true;

    // Common background noise interjections and filler words
    // (patterns that appear repeatedly or alone with no meaningful content)
    const noisePatterns = [
      /^(?:uh|um|ah|oh|hmm|er|erm|uhh|umm|ahh|ohhh)+\s*$/,  // just filler words
      /^(?:um|uh)(\s+(?:um|uh))+\s*$/,                        // repeated filler words
      /^(?:yeah|yep|yup|nope|nah|mmm|huh|eh|shh)+\s*$/,       // common interjections alone
      /^[aeiou]{3,}$/,                                         // long vowel sounds (aaaa, uuuu)
      /^([a-z])\1{2,}\s*$/,                                    // repeated character (aaa, bbb)
      /^[a-z](\s+[a-z])+\s*$/,                                  // just single letters separated by whitespace (e.g. "a b c")
      /^(?:ggs|pfft|tsk|shhh|brr|shh)+\s*$/,                  // exclamations/sound effects
    ];

    if (noisePatterns.some(pattern => pattern.test(trimmed))) {
      return true;
    }

    // Check for predominantly filler words (more than 30% filler) — stricter to catch background chatter
    const fillerWords = ['um', 'uh', 'ah', 'oh', 'hmm', 'er', 'erm', 'like', 'uh', 'uhh', 'umm'];
    const words = trimmed.split(/\s+/);
    const fillerCount = words.filter(w => fillerWords.includes(w)).length;
    if (words.length > 0 && fillerCount / words.length > 0.3) {
      return true;
    }

    // Check for gibberish-like text (very low consonant-to-vowel ratio or unusual patterns)
    const vowels = (trimmed.match(/[aeiou]/gi) || []).length;
    const consonants = (trimmed.match(/[bcdfghjklmnprstvwxyz]/gi) || []).length;
    const totalLetters = vowels + consonants;
    
    // If mostly vowels or mostly consonants, likely noise
    if (totalLetters > 3 && (vowels / totalLetters > 0.8 || consonants / totalLetters > 0.8)) {
      return true;
    }

    return false;
  }, []);

  // ── Send transcript to AI ─────────────────────────────────────────────────
  const sendToAI = useCallback(async (text) => {
    // When keepMicActive is true the finally block keeps the mic in listening
    // state instead of resetting to idle. Used after showing the file list so
    // the user can say a number without repeating "activate".
    let keepMicActive = false;
    try {
      console.log("[Voice Debug] Heard input before interpretation:", text);
      const commandText = normalizeCommandText(text);
      console.log("[Voice Debug] Interpreted command:", commandText);
      if (!commandText) {
        addLog("⚠️ Command was empty after wake-word cleanup.", "warn");
        setPendingAction(null);
        showAiResponse(t.didntCatch);
        return;
      }

      // Prioritize numeric file selection while recent-files mode is active.
      // This must run before generic noise/length filters because ASR often
      // returns short variants (for example, "to" for "two").
      const capturedPending = pendingActionRef.current;
      if (capturedPending?.type === "SHOW_RECENT_FILES") {
        const num = parseFileSelectionNumber(commandText);
        if (num !== null) {
          const files = capturedPending.files ?? [];
          if (num >= 1 && num <= files.length) {
            addLog(`📂 Opening file ${num}: "${files[num - 1].name}"`, "voice");
            showTranscript(commandText);
            setPendingAction(null);
            openDocumentPreview(files[num - 1]);
            return;
          }

          // Number out of range — keep the list visible and require reactivation.
          addLog(`⚠️ File number ${num} is not in the list (1–${files.length}).`, "warn");
          showTranscript(commandText);
          showAiResponse(`There are only ${files.length} files. Say a number between 1 and ${files.length}.`);
          setPendingAction(capturedPending);
          return;
        }
      }

      // ── Check for blank audio or background noise ──────────────────────
      if (isBlankAudioOrNoise(commandText)) {
        addLog("🔊 Detected blank audio or background noise — requiring reactivation.", "warn");
        if (capturedPending?.type === "SHOW_RECENT_FILES") {
          showAiResponse(t.fileSelectionPrompt);
          setPendingAction(capturedPending);
        } else {
          setPendingAction(null);
          showAiResponse(tpl(t.noClearCommandTemplate, { word: activationWordRef.current }));
          // Return to idle/wake-word mode — requires reactivation
          resumeListeningRef.current?.();
        }
        return;
      }

      // ── Require minimum command length (filter out single-word stray background noise) ─
      // ── Require minimum command length (but allow known single-word commands) ─
      const commandWords = commandText.trim().split(/\s+/);
      const knownSingleWordCommands = ['iris', 'activate', 'next', 'previous', 'back', 'help', 'menu', 'close', 'print', 'reminders', 'summary', 'admin', 'summarize'];
      const isSingleWordCommand = commandWords.length === 1 && (knownSingleWordCommands.includes(commandWords[0].toLowerCase()) || /^\d+$/.test(commandWords[0]));
      const requiresTwoWords = !isSingleWordCommand && commandWords.length < 2;
      
      if (requiresTwoWords) {
        addLog("⏱️ Command too brief — likely background noise.", "warn");
        if (capturedPending?.type === "SHOW_RECENT_FILES") {
          showAiResponse(t.fileSelectionPrompt);
          setPendingAction(capturedPending);
        } else {
          setPendingAction(null);
          showAiResponse(t.commandTooBrief);
          resumeListeningRef.current?.();
        }
        return;
      }

      // If a PDF is open, execute common viewer commands locally so commands
      // always apply to the active document without requiring any document ID.
      const localPdf = pdfViewerRef.current;
      if (localPdf && openPdf) {
        const lowered = commandText.toLowerCase().trim();

        if (/\b(?:next page|go to next page|page down)\b/.test(lowered)) {
          addLog("📄 Command: next page", "voice");
          setAiResponse(null);
          setPendingAction(null);
          localPdf.nextPage();
          return;
        }

        if (/\b(?:previous page|prev page|go to previous page|back page)\b/.test(lowered)) {
          addLog("📄 Command: previous page", "voice");
          setAiResponse(null);
          setPendingAction(null);
          localPdf.prevPage();
          return;
        }

        const isSummarizeCommand = /\b(?:summarize|resumir|summary|resumen)\b/i.test(lowered);
        if (!isSummarizeCommand) {
          const goToPageMatch = lowered.match(/\b(?:go to|goto|open)\s*page\s+(\d{1,4})\b/) || lowered.match(/^\s*#?page\s+(\d{1,4})\s*$/) || lowered.match(/^\s*#?(\d{1,4})\s*$/);
          if (goToPageMatch) {
            const pageNumber = parseInt(goToPageMatch[1], 10);
            if (!Number.isNaN(pageNumber)) {
              addLog(`📄 Command: go to page ${pageNumber}`, "voice");
              setAiResponse(null);
              setPendingAction(null);
              localPdf.goToPage(pageNumber);
              return;
            }
          }
        }

        if (/\b(?:remove|clear)\s+(?:all\s+)?highlights?\b|\b(?:borrar|eliminar|quitar|limpiar)\s+(?:todos?\s+(?:los\s+)?)?resaltados?\b/.test(lowered)) {
          addLog("🗑️ Command: remove all highlights", "voice");
          setAiResponse(null);
          setPendingAction(null);
          localPdf.removeHighlights();
          return;
        }

        if (/(?:print|imprimir).*(?:highlight|resalt|destac|marcad|subray)/i.test(lowered) ||
            /(?:highlight|resalt|destac|marcad|subray).*(?:print|imprimir)/i.test(lowered)) {
          addLog("🖨️ Command: print highlighted pages", "voice");
          setAiResponse(null);
          setPendingAction(null);
          localPdf.printHighlightedPages();
          return;
        }

        if (/\b(?:print|imprimir)\s+(?:the\s+|el\s+|este\s+)?(?:document|documento|pdf)\b|^(?:print|imprimir)$/i.test(lowered)) {
          addLog("🖨️ Command: print document", "voice");
          setAiResponse(null);
          setPendingAction(null);
          localPdf.printDocument();
          return;
        }

        if (/\bclose\s+(?:the\s+)?(?:document|pdf)\b|\bcerrar\s+(?:el\s+)?(?:documento|pdf)\b/.test(lowered)) {
          addLog("📄 Command: close document", "voice");
          setAiResponse(null);
          setPendingAction(null);
          setOpenPdf(null);
          return;
        }

        const highlightMatch = commandText.match(/\b(?:highlight|resaltar)\s+(.+)$/i);
        if (highlightMatch) {
          const phrase = highlightMatch[1]
            .replace(/^(?:the|el|la|los|las)\s+/i, "")
            .replace(/\s+please\.?$/i, "")
            .trim();

          if (phrase) {
            const quotedTerms = Array.from(phrase.matchAll(/"([^"]+)"|'([^']+)'/g))
              .map((m) => (m[1] || m[2] || "").trim())
              .filter(Boolean);

            let terms = [];
            if (quotedTerms.length > 0) {
              terms = quotedTerms;
            } else if (phrase.includes(",")) {
              terms = phrase.split(",").map((part) => part.trim()).filter(Boolean);
            } else if (/\s+and\s+/i.test(phrase)) {
              terms = phrase.split(/\s+and\s+/i).map((part) => part.trim()).filter(Boolean);
            } else {
              terms = [phrase];
            }

            // Strip leading/trailing punctuation from each term (e.g., "patient." → "patient")
            terms = terms
              .map((term) => term.replace(/^[^\w]+|[^\w]+$/g, "").trim())
              .filter(Boolean);

            if (terms.length > 0) {
              addLog(`🧪 Parsed highlight terms: ${terms.map((t) => `"${t}"`).join(", ")}`, "info");
              terms.forEach((term) => localPdf.highlightWord(term));
              addLog(`🖊️ Command: highlight ${terms.map((t) => `"${t}"`).join(", ")}`, "voice");
              setAiResponse(null);
              setPendingAction(null);
              return;
            }
          }
        }
      }

      // Reset the advanced-model badge; it will be updated once the actual response arrives.
      setUsedAdvancedModel(false);
      addLog(`🤖 Sending to AI: \"${commandText}\"`, "info");
      const processRequestBody = await buildProcessRequestBody(commandText);
      const data = await fetchJsonWithTimeout("/api/process", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(processRequestBody),
      }, API_TIMEOUT_MS);
      const answer = (data.response || "").trim();
      const usedAdvanced = data?.usedAdvancedModel ?? data?.UsedAdvancedModel ?? data?.usedAdvanced ?? false;

      if (!answer) {
        addLog("⚠️ AI returned an empty response.", "warn");
        setPendingAction(null);
        showAiResponse("I didn't get a response for that command. Please try again.");
        setUsedAdvancedModel(usedAdvanced);
        return;
      }

      // ── Action token interception ────────────────────────────────────────
      if (answer === "__ACTION:OPEN_FILE_PICKER__" || answer === "__ACTION:SHOW_RECENT_FILES__") {
        addLog("📂 Command: show recent uploaded files", "voice");
        setAiResponse(null);
        await fetchRecentDocuments();
        // File list stays visible; user must reactivate to say a number.
      } else if (answer.startsWith("__ACTION:SELECT_DOCUMENT:")) {
        const parts = answer.split(":");
        const docId   = parts[2];
        const docName = parts.slice(3).join(":") || "Document";
        addLog(`📄 Command: select document "${docName}"`, "voice");
        setAiResponse(null);
        setPendingAction(null);
        openDocumentPreview({ previewId: docId, name: docName });
      } else if (answer === "__ACTION:NEXT_PDF_PAGE__") {
        addLog("📄 Command: next page", "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current) {
          pdfViewerRef.current.nextPage();
        } else {
          showAiResponse("No document is open. Open a document first.");
        }
      } else if (answer === "__ACTION:PREVIOUS_PDF_PAGE__") {
        addLog("📄 Command: previous page", "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current) {
          pdfViewerRef.current.prevPage();
        } else {
          showAiResponse("No document is open. Open a document first.");
        }
      } else if (answer.startsWith("__ACTION:HIGHLIGHT_WORD:")) {
        const word = answer.replace("__ACTION:HIGHLIGHT_WORD:", "").replace(/__$/, "").trim();
        addLog(`🖊️ Command: highlight "${word}"`, "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current && word) {
          pdfViewerRef.current.highlightWord(word);
        } else if (!pdfViewerRef.current) {
          showAiResponse("No document is open. Open a document first.");
        }
      } else if (answer === "__ACTION:REMOVE_HIGHLIGHTS__") {
        addLog("🗑️ Command: remove all highlights", "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current) {
          pdfViewerRef.current.removeHighlights();
        } else {
          showAiResponse("No document is open.");
        }
      } else if (answer.startsWith("__ACTION:GO_TO_PAGE:")) {
        const rawPage = answer.replace("__ACTION:GO_TO_PAGE:", "").replace(/__$/, "").trim();
        const pageNumber = parseInt(rawPage, 10);
        addLog(`📄 Command: go to page ${pageNumber}`, "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current && !Number.isNaN(pageNumber)) {
          pdfViewerRef.current.goToPage(pageNumber);
        } else if (!pdfViewerRef.current) {
          showAiResponse("No document is open. Open a document first.");
        } else {
          showAiResponse("Sorry, I couldn't understand the page number.");
        }
      } else if (answer.startsWith("__ACTION:GO_TO_LOINC:")) {
        const loincCode = answer.replace("__ACTION:GO_TO_LOINC:", "").replace(/__$/, "").trim();
        addLog(`🔬 Command: go to LOINC code "${loincCode}"`, "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current && loincCode) {
          pdfViewerRef.current.goToLoincCode(loincCode);
        } else if (!pdfViewerRef.current) {
          showAiResponse("No document is open. Open a document first.");
        }
      } else if (answer === "__ACTION:CLOSE_DOCUMENT__") {
        addLog("📄 Command: close document", "voice");
        setAiResponse(null);
        setPendingAction(null);
        setOpenPdf(null);
      } else if (answer === "__ACTION:OPEN_ADMIN_PANEL__") {
        addLog("🔧 Command: open admin panel", "voice");
        setAiResponse(null);
        setPendingAction(null);
        window.location.href = "/admin.html";
      } else if (answer === "__ACTION:PRINT_DOCUMENT__") {
        addLog("🖨️ Command: print document", "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current) {
          await pdfViewerRef.current.printDocument();
        } else {
          showAiResponse("No document is open. Open a document first.");
        }
      } else if (answer === "__ACTION:PRINT_HIGHLIGHTED_PAGES__") {
        addLog("🖨️ Command: print highlighted pages", "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current) {
          await pdfViewerRef.current.printHighlightedPages();
        } else {
          showAiResponse("No document is open. Open a document first.");
        }
      } else if (answer === "__ACTION:SUMMARIZE_CURRENT_PAGE__") {
        await summarizeCurrentPage();
      } else if (answer.startsWith("__ACTION:SUMMARIZE_PAGE:")) {
        const pageNumStr = answer.replace("__ACTION:SUMMARIZE_PAGE:", "").replace(/__$/, "").trim();
        const pageNum = parseInt(pageNumStr, 10);
        await summarizeCurrentPage(Number.isNaN(pageNum) ? null : pageNum);
      } else if (answer.startsWith("__ACTION:SHOW_REMINDERS:")) {
        addLog("📝 Command: show reminders", "voice");
        setAiResponse(null);
        setPendingAction(null);
        const jsonStr = answer.replace("__ACTION:SHOW_REMINDERS:", "").replace(/__$/, "").trim();
        try {
          const reminders = JSON.parse(jsonStr);
          showRemindersToast(Array.isArray(reminders) ? reminders : []);
        } catch (_) {
          showAiResponse("Couldn't parse reminders. Please try again.");
        }
      } else {
        addLog(`💡 AI: "${answer}"`, "voice");
        setPendingAction(null);
        showAiResponse(answer);
        setUsedAdvancedModel(usedAdvanced);
      }
    } catch (err) {
      const errorMessage = err?.message || "Unknown error";
      addLog(`❌ AI error: ${errorMessage}`, "error");
      setUsedAdvancedModel(false);
      setPendingAction(null);
      showAiResponse(/timed out/i.test(errorMessage)
        ? "The request timed out. Please try again."
        : "I couldn't process that command right now. Please try again.");
    } finally {
      if (keepMicActive) {
        // Stay in listening state so the user can say a file number directly.
        activatedRef.current = true;
        setStatus("listening");
        if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
        listeningTimeoutRef.current = setTimeout(() => {
          if (activatedRef.current) {
            addLog("⏱️ File selection timed out — resetting.", "warn");
            setPendingAction(null);
            resumeListeningRef.current();
          }
        }, FILE_SELECTION_TIMEOUT_MS);
      } else {
        resumeListeningRef.current();
      }
    }
  }, [addLog, buildProcessRequestBody, fetchRecentDocuments, fetchJsonWithTimeout, isBlankAudioOrNoise, normalizeCommandText, openDocumentPreview, openPdf, setPendingAction, showAiResponse, showRemindersToast, showTranscript]);

  sendToAIRef.current = sendToAI;
  useEffect(() => { sendToAIRef.current = sendToAI; }, [sendToAI]);

  const transcribeCommandWithGroqRef = useRef(transcribeCommandWithGroq);
  useEffect(() => { transcribeCommandWithGroqRef.current = transcribeCommandWithGroq; }, [transcribeCommandWithGroq]);

  const transcribeWakewordWithGroqRef = useRef(transcribeWakewordWithGroq);
  useEffect(() => { transcribeWakewordWithGroqRef.current = transcribeWakewordWithGroq; }, [transcribeWakewordWithGroq]);

  // ── Voice pipeline: Groq API + AudioContext mic capture ────────────────
  //
  // All transcription goes through the server-side Groq Whisper API.
  //   'wake-word' — short bursts sent to /api/process/transcribe-wakeword; checked for keyword
  //   'partial'   — each speech burst during a command; transcribed via Groq for streaming UI
  //   'command'   — full accumulated audio; final transcription via Groq
  //
  // Sentence-end detection: Groq Whisper naturally adds `.` `?` `!` when it
  // hears a complete thought.  We detect these patterns and submit the command
  // after a short pause (SENTENCE_END_MS) rather than waiting for the full
  // force-submit timeout.
  useEffect(() => {
    if (mode !== "voice") {
      setWakeWordReady(false);
      return undefined;
    }

    const SAMPLE_RATE          = 16_000;
    const BUFFER_SIZE          = 1024;       // 64 ms per AudioWorklet callback
    const RMS_THRESHOLD        = 0.007;      // RMS below this → silence (lowered for better speaker detection)
    const WAKE_SILENCE_MS      = 400;        // wake-word path: 400ms to capture "hi iris" as one chunk
    const COMMAND_SILENCE_MS   = 1_200;      // command path: 1.2s delay for better speech capture and noise tolerance
    const SENTENCE_END_MS      = 350;        // pause after sentence-end pattern → submit
    const FORCE_SUBMIT_MS      = 4_000;      // max silence during command → force submit (increased for longer pauses)
    const MIN_COMMAND_SEC      = 0.3;        // minimum audio duration (s) to bother submitting
    const MAX_COMMAND_WINDOW_MS = 7_000;     // hard cap after activation so noise cannot keep command mode open forever
    const MAX_COMMAND_AUDIO_SEC = 8.0;       // max accumulated command audio before forced finalization
    const WAKE_MIN_CHUNK_SEC   = 0.25;       // wake-word path: require at least 250ms of audio to prevent noise bursts from triggering wake check
    const COMMAND_MIN_CHUNK_SEC = 0.35;      // command path: larger chunks improve recognition stability

    let disposed = false;

    // Per-effect mutable state (local vars, no re-render on change)
    let speechChunks  = [];   // Float32Array chunks of the current speech burst
    let preSpeechBuffer = []; // Rolling buffer of the last ~500ms of audio before speech
    const MAX_PRE_SPEECH_CHUNKS = Math.ceil((SAMPLE_RATE * 0.5) / BUFFER_SIZE);
    let commandChunks = [];   // all audio accumulated since activation
    let commandSampleCount = 0;
    let commandStartedAt = 0;
    let partialTexts  = [];   // Groq partial transcriptions so far
    let isSpeaking    = false;
    let silenceSince  = 0;
    let lastWakeCheckTime = 0;
    let wakewordInFlight = false; // prevent overlapping wake-word API calls
    let pendingWakeAudio = null;   // queued audio when a wake-word call is in flight

    let forceSubmitTimer    = null;
    let sentenceEndTimer    = null;
    let submitInFlight      = false;

    function concatF32(chunks) {
      const total = chunks.reduce((s, c) => s + c.length, 0);
      const out = new Float32Array(total);
      let off = 0;
      for (const c of chunks) { out.set(c, off); off += c.length; }
      return out;
    }

    function totalSamples(chunks) {
      return chunks.reduce((sum, c) => sum + c.length, 0);
    }

    function isSentenceComplete(text) {
      if (!text) return false;
      const t = text.trim();
      if (/[.?!]\s*$/.test(t)) return true;
      if (/^(next|previous|prev)\s+(page|slide)$/i.test(t)) return true;
      if (/^(close|open)\s+(document|file|pdf|admin)$/i.test(t)) return true;
      if (/^(clear|remove)\s+(all\s+)?highlights?$/i.test(t)) return true;
      if (/^print(\s+(document|pdf))?$/i.test(t)) return true;
      if (/^print\s+((only|just)\s+)?(highlighted\s+pages?|pages\s+with\s+highlights?|highlights\s+only)$/i.test(t)) return true;
      if (/^what('s|\s+is)\s+the\s+time\??$/i.test(t)) return true;
      if (/^show(\s+my|\s+all)?\s+reminders?$/i.test(t)) return true;
      if (/^(open\s+)?admin(\s+panel)?$/i.test(t)) return true;
      return false;
    }

    function clearTimers() {
      if (forceSubmitTimer)   { clearTimeout(forceSubmitTimer);   forceSubmitTimer   = null; }
      if (sentenceEndTimer)   { clearTimeout(sentenceEndTimer);   sentenceEndTimer   = null; }
      if (commandSubmitTimeoutRef.current) {
        clearTimeout(commandSubmitTimeoutRef.current);
        commandSubmitTimeoutRef.current = null;
      }
    }

    async function submitCommand(reason = "normal") {
      if (disposed || !activatedRef.current || submitInFlight) return;
      submitInFlight = true;
      clearTimers();
      const audio   = concatF32(commandChunks);
      commandChunks = [];
      commandSampleCount = 0;
      commandStartedAt = 0;
      const partial = partialTexts.join(" ").trim();
      partialTexts  = [];

      if (audio.length < SAMPLE_RATE * MIN_COMMAND_SEC) {
        if (partial) {
          activatedRef.current = false;
          showTranscript(partial);
          setAiResponse(null);
          setStatus("sending");
          sendToAIRef.current?.(partial);
        } else {
          resumeListeningRef.current();
        }
        submitInFlight = false;
        return;
      }

      activatedRef.current = false;
      if (reason === "max-window") {
        addLog("⏱️ Max command window reached — finalizing command.", "warn");
      } else if (reason === "max-audio") {
        addLog("⏱️ Max command audio reached — finalizing command.", "warn");
      }
      if (partial) showTranscript(partial);
      setStatus("processing");

      // Send to Groq for final transcription
      try {
        const text = await transcribeCommandWithGroqRef.current?.(audio, { isFinal: true });
        if (text && text.length >= 2 && /[A-Za-z0-9]/.test(text)) {
          addLog(`🗣️ Groq: "${text}"`, "voice");
          showTranscript(text);
          setAiResponse(null);
          setStatus("sending");
          sendToAIRef.current?.(text);
        } else {
          addLog("(No speech detected in command or transcription deferred by rate limit)", "warn");
          resumeListeningRef.current();
        }
      } catch (err) {
        addLog(`❌ Transcription error: ${err?.message || "unknown"}`, "error");
        resumeListeningRef.current();
      } finally {
        submitInFlight = false;
      }
    }

    // Handle wake-word transcription result from Groq
    function handleWakeWordResult(cleaned, trailingCommandOverride = "") {
      if (disposed) return;
      const word = activationWordRef.current;
      // Normalize: strip punctuation, collapse whitespace, lowercase for comparison.
      // Groq may return "Hi, Iris.", "hi iris", "Iris!", "iris", etc.
      // stripDiacritics() is called first so accented letters are reduced to their
      // ASCII base (e.g. "ú"→"u", "número"→"numero") before the non-ASCII filter
      // removes remaining special characters.  Without this the old approach removed
      // "ú" entirely, turning "número" into "nmero" instead of "numero", which broke
      // prefix matching for Spanish file-selection commands.
      const normalize = (s) => stripDiacritics((s || "").toLowerCase()).replace(/[^a-z0-9\s]/g, "").replace(/\s+/g, " ").trim();
      const normalizedText = normalize(cleaned);
      const normalizedWord = normalize(word || "Iris");
      const coreWord = (normalizedWord.split(" ").pop() || "iris").trim();

      if (!normalizedText || !coreWord || coreWord.length < 2) return;

      const wordEsc = coreWord.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      const matched = new RegExp(`\\b${wordEsc}\\b`, "i").test(normalizedText);
      addLog(`🔍 Wake check: "${normalizedText}" vs "${coreWord}" → ${matched ? "MATCH ✅" : "no match"}`, "info");
      console.log(`[Debug Voice]: handle wake word result: normalizedText: "${normalizedText}", coreWord: "${coreWord}", matched: ${matched}`);

      if (
        matched &&
        !activatedRef.current &&
        statusRef.current !== "sending" &&
        statusRef.current !== "processing"
      ) {
        // Remove the wake-word from the transcription to get any trailing command
        const overrideRest = normalize(trailingCommandOverride || "");
        const rest = overrideRest || normalizedText.replace(normalizedWord, "").replace(coreWord, "").trim();
        activatedRef.current = true;
        commandChunks = [...speechChunks];
        commandSampleCount = totalSamples(commandChunks);
        commandStartedAt = Date.now();
        speechChunks = [];
        partialTexts  = [];
        clearTimers();
        setStatus("listening");
        setShowLanding(false);
        addLog("🎤 Activated — speak your command", "info");

        if (rest.length >= 2) {
          partialTexts.push(rest);
          showTranscript(rest);
          if (isSentenceComplete(rest)) {
            sentenceEndTimer = setTimeout(submitCommand, SENTENCE_END_MS);
          } else {
            forceSubmitTimer = setTimeout(submitCommand, FORCE_SUBMIT_MS);
          }
        } else {
          if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
          listeningTimeoutRef.current = setTimeout(() => {
            if (activatedRef.current && commandChunks.length === 0) {
              addLog("⏱️ No command heard — resetting.", "warn");
              resumeListeningRef.current();
            }
          }, LISTENING_IDLE_TIMEOUT_MS);
        }
      }
    }

    // Handle partial transcription result from Groq
    function handlePartialResult(cleaned) {
      if (disposed || !activatedRef.current) return;
      if (cleaned.length >= 2) {
        partialTexts.push(cleaned);
        setTranscript(partialTexts.join(" "));

        if (isSentenceComplete(cleaned)) {
          clearTimers();
          sentenceEndTimer = setTimeout(submitCommand, SENTENCE_END_MS);
        } else {
          if (forceSubmitTimer) clearTimeout(forceSubmitTimer);
          forceSubmitTimer = setTimeout(submitCommand, FORCE_SUBMIT_MS);
        }
      }
    }

    // Start the mic capture loop
    async function startMic() {
      addLog("🔄 Starting microphone...", "info");
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        if (disposed) { stream.getTracks().forEach(t => t.stop()); return; }
        addLog("✅ Mic permission granted", "info");

        streamRef.current = stream;
        const ctx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: SAMPLE_RATE });
        audioCtxRef.current = ctx;
        addLog(`🔊 AudioContext state: ${ctx.state}, sampleRate: ${ctx.sampleRate}`, "info");

        // Mobile Chrome requires explicit resume after user gesture (getUserMedia)
        if (ctx.state === 'suspended') {
          addLog("⏸️ AudioContext suspended — resuming...", "warn");
          await ctx.resume();
          addLog(`🔊 AudioContext resumed — state: ${ctx.state}`, "info");
        }

        const source    = ctx.createMediaStreamSource(stream);

        // Add AudioWorklet module
        await ctx.audioWorklet.addModule('/audioProcessor.js');
        const processor = new AudioWorkletNode(ctx, 'audio-processor');
        processorRef.current = processor;

        source.connect(processor);
        processor.connect(ctx.destination);

        // Track speech detection diagnostics
        let speechBurstCount = 0;
        let lastDiagTime = Date.now();
        let micInitializedAt = Date.now();

        processor.port.onmessage = (ev) => {
          if (disposed) return;
          const samples = ev.data;
          const rms = Math.sqrt(samples.reduce((s, v) => s + v * v, 0) / samples.length);
          const now = Date.now();

          // Hard-stop command capture if it runs too long (usually caused by sustained background noise).
          if (activatedRef.current && commandStartedAt > 0) {
            const commandElapsedMs = now - commandStartedAt;
            if (commandElapsedMs >= MAX_COMMAND_WINDOW_MS) {
              if (!submitInFlight) {
                if (partialTexts.join(" ").trim() || commandSampleCount >= SAMPLE_RATE * MIN_COMMAND_SEC) {
                  void submitCommand("max-window");
                } else {
                  addLog("⏱️ Ignoring long background noise — resetting command mode.", "warn");
                  resumeListeningRef.current();
                }
              }
              return;
            }
          }

          // Periodic diagnostic: log every 10 seconds to show audio is flowing
          if (now - lastDiagTime > 10000) {
            addLog(`🔈 Audio flowing — RMS: ${rms.toFixed(4)}, speech bursts: ${speechBurstCount}, activated: ${activatedRef.current}`, "info");
            lastDiagTime = now;
            speechBurstCount = 0;
          }

          if (rms >= RMS_THRESHOLD) {
            if (!isSpeaking) {
              speechBurstCount++;
              speechChunks.push(...preSpeechBuffer);
              if (activatedRef.current) {
                commandChunks.push(...preSpeechBuffer);
              }
              preSpeechBuffer = [];
            }
            isSpeaking  = true;
            silenceSince = 0;
            speechChunks.push(samples);
            if (activatedRef.current) {
              if (commandStartedAt === 0) commandStartedAt = now;
              commandChunks.push(samples);
              commandSampleCount += samples.length;
              if (commandSampleCount >= SAMPLE_RATE * MAX_COMMAND_AUDIO_SEC && !submitInFlight) {
                void submitCommand("max-audio");
                return;
              }
            }
          } else if (isSpeaking) {
            if (!silenceSince) silenceSince = now;
            const silenceTargetMs = activatedRef.current ? COMMAND_SILENCE_MS : WAKE_SILENCE_MS;
            if ((now - silenceSince) >= silenceTargetMs) {
              isSpeaking = false;
              if (speechChunks.length > 0) {
                const audio = concatF32(speechChunks);
                speechChunks = [];
                silenceSince = 0;
                const minChunkSec = activatedRef.current ? COMMAND_MIN_CHUNK_SEC : WAKE_MIN_CHUNK_SEC;
                if (audio.length >= SAMPLE_RATE * minChunkSec) {
                  const durationSec = (audio.length / SAMPLE_RATE).toFixed(2);
                  addLog(`🎙️ Speech chunk: ${durationSec}s, activated: ${activatedRef.current}`, "info");
                  if (activatedRef.current) {
                    // Partial transcription during command — send to Groq
                    (async () => {
                      try {
                        const text = await transcribeCommandWithGroqRef.current?.(audio);
                        if (text && !disposed) handlePartialResult(text.trim());
                      } catch (_) {}
                    })();
                  } else {
                    // Wake-word check — send to Groq (queue latest if in-flight)
                    async function sendWakeAudio(chunk) {
                      if (Date.now() - micInitializedAt < 1500) {
                        return;
                      }
                      wakewordInFlight = true;
                      try {
                        const result = await transcribeWakewordWithGroqRef.current?.(chunk);
                        if (!disposed) {
                          if (result?.text) {
                            handleWakeWordResult(result.text.trim(), result.commandAfterWakeword || "");
                          }
                        }
                      } catch (_) {}
                      finally {
                        wakewordInFlight = false;
                        // If a chunk was queued while we were in-flight, send it now
                        if (pendingWakeAudio && !disposed && !activatedRef.current) {
                          const queued = pendingWakeAudio;
                          pendingWakeAudio = null;
                          sendWakeAudio(queued);
                        }
                      }
                    }
                    if (!wakewordInFlight) {
                      sendWakeAudio(audio);
                    } else {
                      pendingWakeAudio = audio; // keep latest, discard older
                    }
                  }
                }
              }
            }
          } else {
            preSpeechBuffer.push(samples);
            if (preSpeechBuffer.length > MAX_PRE_SPEECH_CHUNKS) {
              preSpeechBuffer.shift();
            }
          }
        };

        if (!disposed) {
          setWakeWordReady(true);
          addLog(`🎤 Microphone ready — say "${activationWordRef.current}"`, "info");
        }
      } catch (err) {
        if (!disposed) {
          addLog(`❌ Mic error: ${err.message || err}`, "error");
          setWakeWordReady(false);
        }
      }
    }

    startMic();

    return () => {
      disposed = true;
      clearTimers();
      try {
        if (processorRef.current) {
          processorRef.current.port.close();
          processorRef.current.disconnect();
        }
      } catch (_) {}
      try { audioCtxRef.current?.close(); } catch (_) {}
      if (streamRef.current) {
        streamRef.current.getTracks().forEach(t => t.stop());
        streamRef.current = null;
      }
      audioCtxRef.current  = null;
      processorRef.current = null;
      setWakeWordReady(false);
    };
  }, [mode]);

  // Keepalive control is server-side and managed from the admin panel toggle.

  useEffect(() => {
    return () => {
      if (listeningTimeoutRef.current)     clearTimeout(listeningTimeoutRef.current);
      if (commandSubmitTimeoutRef.current) clearTimeout(commandSubmitTimeoutRef.current);
      if (remindersTimerRef.current)       clearTimeout(remindersTimerRef.current);
      if (remindersFadeTimerRef.current)   clearTimeout(remindersFadeTimerRef.current);
    };
  }, []);

  // ── Manual tap button ─────────────────────────────────────────────────────
  const handleMicButton = () => {
    if (status === "idle") {
      if (!wakeWordReady) {
        addLog("⚠️ Microphone not ready yet.", "warn");
        return;
      }
      activatedRef.current = true;
      setStatus("listening");
      setTranscript(null);
      setAiResponse(null);
      addLog("🎤 Listening… speak your command", "info");
      if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
      listeningTimeoutRef.current = setTimeout(() => {
        if (activatedRef.current) {
          addLog("⏱️ No speech detected — resetting.", "warn");
          resumeListening();
        }
      }, LISTENING_IDLE_TIMEOUT_MS);
    } else if (status === "listening") {
      if (listeningTimeoutRef.current) { clearTimeout(listeningTimeoutRef.current); listeningTimeoutRef.current = null; }
      activatedRef.current = false;
      setStatus("idle");
      addLog("Cancelled.", "info");
    }
  };

  const handleTextSubmit = (e) => {
    e.preventDefault();
    const text = inputText.trim();
    if (!text) return;
    setInputText("");
    setStatus("sending");
    setTranscript(null);
    setAiResponse(null);
    showTranscript(text);
    sendToAI(text);
  };

  // ── Render ────────────────────────────────────────────────────────────────
  const isListening  = status === "listening";
  const isProcessing = status === "processing";
  const isSending    = status === "sending";
  const btnLabel = isSending
    ? t.sendingAI
    : isProcessing
      ? t.transcribing
      : isListening
        ? t.listening
        : !wakeWordReady
          ? t.connectingMicShort
          : tpl(t.sayWordTemplate, { word: activationWord });
  const dotColor = isSending    ? "#3498db"
    : isProcessing ? "#9b59b6"
    : isListening  ? "#e67e22"
    : !wakeWordReady ? "#f97316"
    : "#00b894";

  const shouldPulse = isProcessing || isSending || !wakeWordReady;
  const dotStyle = { ...styles.dot, background: dotColor, ...(shouldPulse ? { animation: "pulse 1s ease-in-out infinite" } : {}) };

  return (
    <div style={{ ...styles.page, ...(mode === "admin" ? styles.adminMode : {}) }}>
      {/* ── Landing screen — shown until first activation ─────────────────────── */}
      {mode === "voice" && showLanding && (
        <div style={styles.landing}>
          <img
            src={glassesImg}
            alt="Smart Glasses"
            className="assistant-logo"
            style={{ display: 'block', maxWidth: '280px', width: '100%', height: 'auto', flexShrink: 0, clipPath: 'inset(0 0 12% 0)' }}
          />
          <div style={styles.landingCard} className="sgm-landing-card-inner">
            {/* ── Title row: title centered in full box, lion at right edge ── */}
            <div style={{ position: 'relative' }} className="sgm-landing-title-row">
              <h1 style={{ ...styles.landingTitle, paddingRight: 76, whiteSpace: 'pre-line' }} className="sgm-landing-title">{t.title}</h1>
              <div className="sgm-landing-logo" style={{
                position: 'absolute',
                right: 0,
                top: '50%',
                transform: 'translateY(-50%)',
                width: 68,
                height: 68,
                backgroundImage: `url(${pennStateEmblemImg})`,
                backgroundSize: '165%',
                backgroundRepeat: 'no-repeat',
                backgroundPosition: 'center',
              }} />
            </div>

            {/* ── Numbered steps + Ready as final step ──────────────────── */}
            <ol style={styles.landingOl} className="sgm-landing-how">
              {t.steps.map((step, i) => (
                <li key={i}>
                  <span>{renderWithHighlight(step, activationWord)}</span>
                  {t.stepSubs[i] && <><br /><span className="sub-line" style={{color:'#aaa',fontSize:'0.88rem'}}>{t.stepSubs[i]}</span></>}
                </li>
              ))}
              {/* Step n+1: initialisation status (loading or Ready) */}
              <li>
                <div style={styles.landingStatus}>
                  {!wakeWordReady ? (
                    <div style={styles.landingLoadingRow}>
                      <div style={styles.landingSpinner} />
                      <span style={styles.landingLoadingText}>{t.connectingMic}</span>
                    </div>
                  ) : (
                    <div style={styles.landingReadyRow}>
                      <span style={styles.landingReadyBadge}>✓</span>
                      <span style={styles.landingReadyText}>{t.readyShort}</span>
                    </div>
                  )}
                </div>
              </li>
            </ol>

            {/* ── Common commands table (hidden on narrow / short screens) ─ */}
            <div style={styles.landingSection} className="sgm-landing-cmds">
              <h2 style={styles.landingH2}>{t.commonCommandsTitle}</h2>
              <table style={styles.landingTable}>
                <tbody>
                  {t.commandList.map(([cmd, desc]) => (
                    <tr key={cmd}>
                      <td style={styles.landingCmd}>"{cmd}"</td>
                      <td style={styles.landingDesc}>{desc}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {mode === "voice" && !showLanding && (
        <div style={styles.indicator} onClick={handleMicButton}>
          <span style={dotStyle} />
          <span style={styles.indicatorText}>{btnLabel}</span>
          {usedAdvancedModel && (
            <div title="Advanced model selected" style={{display:'inline-flex', alignItems:'center', gap:6, marginLeft:6}}>
              <span style={{fontSize:14, color:'#7ec8e3'}}>⚡</span>
            </div>
          )}
        </div>
      )}

      {mode === "voice" && !showLanding && isListening && (
        <div style={styles.commandList}>
          <p style={styles.commandListTitle}>{t.commandsOverlayTitle}</p>
          <div style={styles.commandListGrid}>
            {(t.commandList || []).map(([cmd, desc]) => (
              <div key={cmd} style={styles.commandListRow}>
                <span style={styles.commandListCmd}>"{cmd}"</span>
                <span style={styles.commandListDesc}>{desc}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      <main style={styles.main}>

        {(transcript || isSending || aiResponse || pendingAction) && (
          <section style={styles.conversationArea}>
            {transcript && (
              <div style={styles.transcriptBubble}>
                <span style={styles.bubbleLabel}>{t.you}</span>
                <p style={styles.bubbleText}>{transcript}</p>
              </div>
            )}
            {isSending && !aiResponse && !pendingAction && (
              <div style={styles.thinkingBubble}>
                <span style={styles.thinkingText}>{t.aiThinking}</span>
              </div>
            )}
            {aiResponse && !pendingAction && (
              <div style={styles.aiBubble}>
                <span style={styles.bubbleLabel}>{t.aiLabel}</span>
                <p style={styles.bubbleText}>{aiResponse}</p>
                {usedAdvancedModel && (
                  <div style={{position: 'absolute', right: 12, bottom: 8, background: 'rgba(11,99,166,0.9)', color: 'white', padding: '4px 6px', borderRadius: 6, fontSize: 12, display: 'flex', alignItems: 'center', gap: 6}} title="Generated by advanced model">
                    <span style={{fontSize:12}}>⚡</span>
                    <span style={{fontSize:11}}>{t.advanced}</span>
                  </div>
                )}
              </div>
            )}
            {pendingAction?.type === "SHOW_RECENT_FILES" && (
              <div style={styles.actionCard}>
                <p style={styles.actionCardTitle}>{t.recentFiles}</p>
                <p style={styles.actionCardHint}>{tpl(t.recentFilesHintTemplate, { n: pendingAction.files.length })}</p>
                <div style={styles.actionList}>
                  {pendingAction.files.map((file, index) => (
                    <button
                      key={file.previewId ?? file.id ?? `${file.name}-${index}`}
                      type="button"
                      style={styles.actionListBtn}
                      onClick={() => openDocumentPreview(file)}
                    >
                      <span style={styles.actionListIndex}>{index + 1}</span>
                      <span style={styles.actionListBody}>
                        <span style={styles.actionListName}>{file.name}</span>
                        {file.uploadedAt && (
                          <span style={styles.actionListMeta}>{formatActionTime(file.uploadedAt)}</span>
                        )}
                      </span>
                    </button>
                  ))}
                </div>
              </div>
            )}
            {pendingAction?.type === "SELECT_DOCUMENT" && (
              <div style={styles.actionCard}>
                <p style={styles.actionCardTitle}>{t.lastDocument}</p>
                <p style={styles.actionCardDoc}>{pendingAction.name}</p>
                <button
                  style={styles.actionBtnSecondary}
                  onClick={() => openDocumentPreview({ previewId: pendingAction.id, name: pendingAction.name })}
                >
                  {t.previewDocument}
                </button>
              </div>
            )}
          </section>
        )}

        {/* ── Reminders toast — fades out like a notification ───────────── */}
        {remindersToast && (
          <div style={{
            ...styles.remindersToast,
            opacity:    remindersToast.phase === "fading" ? 0 : 1,
            transition: remindersToast.phase === "fading"
              ? "opacity 0.9s ease-out"
              : "opacity 0.25s ease-in",
          }}>
            <p style={styles.remindersToastTitle}>{t.remindersTitle}</p>
            {remindersToast.reminders.length === 0 ? (
              <p style={styles.remindersToastEmpty}>{t.noReminders}</p>
            ) : (
              <ul style={styles.remindersToastList}>
                {remindersToast.reminders.map((r, i) => (
                  <li key={r.id ?? i} style={styles.remindersToastItem}>
                    <span style={styles.remindersToastBullet}>{i + 1}.</span>
                    <span>{r.text}</span>
                    {r.createdAt && (
                      <span style={styles.remindersToastMeta}>
                        {formatActionTime(r.createdAt)}
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}

        {openPdf && (
          <PdfViewer
            key={openPdf.id}
            ref={pdfViewerRef}
            documentId={openPdf.id}
            documentName={openPdf.name}
            onClose={handleClosePdf}
          />
        )}
      </main>

      {mode === "admin" && (
        <div style={styles.textInputArea}>
          <form onSubmit={handleTextSubmit} style={{ display: 'flex', gap: '10px', width: '100%', maxWidth: '680px', margin: '0 auto' }}>
            <input
              type="text"
              value={inputText}
              onChange={e => setInputText(e.target.value)}
              placeholder={tpl(t.textInputPlaceholderTemplate, { word: activationWord })}
              style={styles.textInput}
              disabled={isSending || isProcessing}
            />
            <button type="submit" disabled={isSending || isProcessing || !inputText.trim()} style={styles.textSubmitBtn}>
              {t.send}
            </button>
          </form>
        </div>
      )}

      {mode === "voice" && (
        <button
          style={styles.logToggleBtn}
          onClick={() => setLogOpen(prev => !prev)}
        >
          {logOpen ? t.closeLog : t.activityLogBtn}
        </button>
      )}

      {mode === "voice" && logOpen && (
        <div style={styles.logPanel}>
          <h2 style={styles.sectionTitle}>{t.activityLogTitle}</h2>
          {logs.length === 0 ? (
            <p style={styles.emptyLog}>{tpl(t.noActivityTemplate, { word: activationWord })}</p>
          ) : (
            <ul style={styles.logList}>
              {logs.map(entry => (
                <li key={entry.id} style={{ ...styles.logItem, background: logBg(entry.type) }}>
                  <span style={styles.logTime}>{entry.ts}</span>
                  <span>{entry.text}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function formatActionTime(value) {
  if (!value) return "";

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "";

  return parsed.toLocaleString();
}

/**
 * Strip diacritical marks from a string so accented characters are reduced to
 * their ASCII base letter (e.g. "ú"→"u", "é"→"e", "ñ"→"n", "número"→"numero").
 * Uses NFD normalization to split a combined character into its base + combining
 * mark(s), then removes the combining marks.
 */
function stripDiacritics(s) {
  return s.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}

// Parses a voice/typed command into a 1-based file-list index.
// Handles bare digits ("2"), "file 2", "number 3", "second", "open the third", etc.
// Returns a number 1–9 when a numeric intent is detected; the caller is
// responsible for bounds-checking against the actual file-list length.
function parseFileSelectionNumber(text) {
  if (!text) return null;
  const s = text.trim().toLowerCase();
  const normalized = s.replace(/[.,!?;:]+$/g, "").trim();
  // Strip diacritics so accented Spanish words match their ASCII counterparts in the
  // prefix/digit patterns below (e.g. Whisper may return "Número 1." for a Spanish
  // speaker saying "número uno", and "número" must match the "numero" prefix).
  const normalizedAscii = stripDiacritics(normalized).replace(/[^a-z0-9\s#]/g, "").trim();

  // Word ordinals / cardinals — English and Spanish (covers the typical 1–5 list size)
  const words = {
    one: 1, first: 1,
    two: 2, second: 2, to: 2, too: 2,
    three: 3, third: 3,
    four: 4, fourth: 4, for: 4,
    five: 5, fifth: 5,
    // Spanish cardinals
    uno: 1, una: 1,
    dos: 2,
    tres: 3,
    cuatro: 4,
    cinco: 5,
    // Spanish ordinals
    primero: 1, primera: 1, primer: 1,
    segundo: 2, segunda: 2,
    tercero: 3, tercera: 3, tercer: 3,
    cuarto: 4, cuarta: 4,
    quinto: 5, quinta: 5,
  };
  for (const [word, num] of Object.entries(words)) {
    if (new RegExp(`\\b${word}\\b`).test(normalized)) return num;
    if (new RegExp(`\\b${word}\\b`).test(normalizedAscii)) return num;
  }

  // Patterns like "file 2", "number 3", "open 4", "#5", "pick 1", "select 2"
  // Also Spanish: "archivo 2", "número 3", "abrir 4", "seleccionar 2"
  // Use the diacritic-stripped text so "número 1" matches the "numero" prefix.
  const prefixMatch = normalizedAscii.match(/(?:file|number|open|pick|select|item|archivo|numero|abrir|seleccionar)\s*#?\s*([1-9])\b/);
  if (prefixMatch) return parseInt(prefixMatch[1], 10);

  // Bare single digit (possibly surrounded by whitespace or punctuation)
  const bareMatch = normalizedAscii.match(/^\s*#?\s*([1-9])\s*$/);
  if (bareMatch) return parseInt(bareMatch[1], 10);

  return null;
}

function logBg(type) {
  return { voice: "#00695c", doc: "#1565c0", error: "#b71c1c", warn: "#e65100" }[type] ?? "#333";
}

const styles = {
  page: {
    height: "100%",
    background: "#000",
    color: "#e0e0e0",
    display: "flex",
    flexDirection: "column",
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
    position: "relative",
    overflow: "hidden",
  },
  adminMode: {
    background: "transparent", // Admin page will provide its own background
    color: "#333", // Match admin panel text color
  },
  // Landing screen overlay
  landing: {
    position: "fixed",
    inset: 0,
    background: "rgba(0,0,0,0.92)",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "flex-start",
    zIndex: 100,
    padding: "max(16px, 3vh) 16px 24px",
    gap: 20,
    overflowY: "auto",
    overflowX: "hidden",
  },
  landingCard: {
    background: "#1d3461",
    border: "1px solid rgba(255,255,255,0.15)",
    borderRadius: 16,
    padding: "min(32px, 5vh) min(28px, 5vw)",
    maxWidth: 620,
    width: "100%",
    display: "flex",
    flexDirection: "column",
    gap: "min(20px, 3vh)",
  },
  landingTitle: {
    margin: 0,
    fontSize: "1.7rem",
    fontWeight: 700,
    color: "#fff",
    textAlign: "center",
    lineHeight: 1.25,
  },
  landingSubtitle: {
    margin: 0,
    fontSize: "1rem",
    color: "#ccc",
    textAlign: "center",
  },
  landingSection: {
    display: "flex",
    flexDirection: "column",
    gap: 10,
  },
  landingH2: {
    margin: 0,
    fontSize: "0.95rem",
    fontWeight: 600,
    color: "#f97316",
    textTransform: "uppercase",
    letterSpacing: "0.06em",
  },
  landingOl: {
    margin: 0,
    paddingLeft: 22,
    display: "flex",
    flexDirection: "column",
    gap: 8,
    color: "#e8eef8",
    fontSize: "0.95rem",
    lineHeight: 1.6,
  },
  landingTable: {
    width: "100%",
    borderCollapse: "collapse",
    fontSize: "0.875rem",
    tableLayout: "fixed",
  },
  landingCmd: {
    padding: "5px 10px 5px 0",
    color: "#f97316",
    fontFamily: "monospace",
    wordBreak: "break-word",
    verticalAlign: "top",
    width: "40%",
  },
  landingDesc: {
    padding: "5px 0",
    color: "#bbb",
    verticalAlign: "top",
  },
  // Initialisation status block rendered as the last step in the list
  landingStatus: {
    marginTop: 4,
    padding: "10px 14px",
    borderRadius: 10,
    background: "rgba(255,255,255,0.09)",
    border: "1px solid rgba(255,255,255,0.15)",
    display: "inline-flex",
    alignItems: "center",
  },
  landingLoadingRow: {
    display: "flex",
    alignItems: "center",
    gap: 12,
  },
  landingSpinner: {
    width: 18,
    height: 18,
    border: "2px solid #333",
    borderTop: "2px solid #f97316",
    borderRadius: "50%",
    flexShrink: 0,
    animation: "spin 0.9s linear infinite",
  },
  landingLoadingText: {
    fontSize: "0.875rem",
    color: "#aaa",
  },
  landingReadyRow: {
    display: "flex",
    alignItems: "center",
    gap: 10,
  },
  landingReadyBadge: {
    width: 28,
    height: 28,
    borderRadius: "50%",
    background: "#00b894",
    color: "#000",
    display: "inline-flex",
    alignItems: "center",
    justifyContent: "center",
    fontWeight: 700,
    fontSize: "1rem",
    flexShrink: 0,
  },
  landingReadyText: {
    fontSize: "1rem",
    color: "#e8eef8",
    fontWeight: 600,
  },
  // Full command list overlay shown at top-left only while the assistant is listening.
  commandList: {
    position: "fixed",
    top: 60,
    left: 12,
    background: "rgba(10,10,10,0.93)",
    border: "1px solid #2a2a2a",
    borderRadius: 10,
    padding: "10px 14px",
    zIndex: 50,
    maxWidth: "min(320px, calc(100vw - 24px))",
    maxHeight: "calc(100vh - 80px)",
    overflowY: "auto",
  },
  commandListTitle: {
    margin: "0 0 8px",
    fontSize: "0.72rem",
    fontWeight: 700,
    color: "#f97316",
    textTransform: "uppercase",
    letterSpacing: "0.06em",
  },
  commandListGrid: {
    display: "flex",
    flexDirection: "column",
    gap: 4,
  },
  commandListRow: {
    display: "flex",
    gap: 8,
    alignItems: "baseline",
  },
  commandListCmd: {
    fontFamily: "monospace",
    fontSize: "0.7rem",
    color: "#f97316",
    whiteSpace: "nowrap",
    flexShrink: 0,
  },
  commandListDesc: {
    fontSize: "0.7rem",
    color: "#777",
  },
  indicator: {
    position: "absolute",
    top: 16,
    right: 16,
    display: "flex",
    alignItems: "center",
    gap: 8,
    zIndex: 40,
  },
  dot: { width: 12, height: 12, borderRadius: "50%" },
  indicatorText: { fontSize: "0.85rem", color: "#ccc" },
  main: {
    flex: 1,
    minHeight: 0,
    maxWidth: 680,
    width: "100%",
    margin: "48px auto 32px",
    padding: "0 16px",
    display: "flex",
    flexDirection: "column",
    gap: 24,
    overflowY: "auto",
  },
  textInputArea: {
    padding: "16px",
    background: "rgba(255,255,255,0.05)",
    borderTop: "1px solid rgba(255,255,255,0.1)",
    display: "flex",
    justifyContent: "center",
  },
  textInput: {
    flex: 1,
    padding: "12px 16px",
    borderRadius: "8px",
    border: "1px solid #cccccc",
    background: "#fff",
    color: "#000",
    fontSize: "1rem",
    outline: "none",
  },
  textSubmitBtn: {
    padding: "0 20px",
    borderRadius: "8px",
    background: "#0b63a6",
    color: "#fff",
    border: "none",
    fontWeight: "bold",
    cursor: "pointer",
  },
  btnWrap: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: 16,
    padding: "32px 0",
  },
  micBtn: {
    width: 120,
    height: 120,
    borderRadius: "50%",
    border: "none",
    fontSize: "3rem",
    cursor: "pointer",
    transition: "background 0.2s",
    boxShadow: "0 4px 24px rgba(0,0,0,0.5)",
  },
  btnHint: { margin: 0, color: "#aaa", fontSize: "0.95rem" },
  conversationArea: {
    display: "flex",
    flexDirection: "column",
    gap: 12,
    padding: "0 4px",
  },
  transcriptBubble: {
    background: "#1e3a5f",
    borderRadius: 12,
    padding: "12px 16px",
    alignSelf: "flex-end",
    maxWidth: "85%",
  },
  aiBubble: {
    background: "#1a3a2a",
    borderRadius: 12,
    padding: "12px 16px",
    alignSelf: "flex-start",
    maxWidth: "85%",
  },
  thinkingBubble: {
    background: "#1a1a2e",
    borderRadius: 12,
    padding: "12px 16px",
    alignSelf: "flex-start",
  },
  bubbleLabel: {
    fontSize: "0.7rem",
    fontWeight: 700,
    textTransform: "uppercase",
    color: "rgba(255,255,255,0.5)",
    letterSpacing: "0.08em",
    display: "block",
    marginBottom: 4,
  },
  bubbleText: {
    margin: 0,
    fontSize: "1rem",
    lineHeight: 1.5,
    color: "#e0e0e0",
  },
  thinkingText: {
    fontSize: "0.9rem",
    color: "#888",
    fontStyle: "italic",
  },
  actionCard: {
    background: "#1a2a3a",
    border: "1px solid #2a4a6a",
    borderRadius: 12,
    padding: "16px 20px",
    alignSelf: "flex-start",
    maxWidth: "85%",
    display: "flex",
    flexDirection: "column",
    gap: 8,
  },
  actionCardTitle: {
    margin: 0,
    fontSize: "0.95rem",
    fontWeight: 700,
    color: "#7ec8e3",
  },
  actionCardHint: {
    margin: 0,
    fontSize: "0.85rem",
    color: "#aaa",
  },
  actionCardDoc: {
    margin: 0,
    fontSize: "1rem",
    color: "#e0e0e0",
    fontWeight: 600,
    wordBreak: "break-all",
  },
  actionList: {
    display: "flex",
    flexDirection: "column",
    gap: 8,
    marginTop: 4,
  },
  actionListBtn: {
    display: "flex",
    alignItems: "center",
    gap: 12,
    width: "100%",
    padding: "10px 12px",
    background: "#14212f",
    color: "#fff",
    border: "1px solid #2a4a6a",
    borderRadius: 10,
    fontSize: "0.9rem",
    cursor: "pointer",
    textAlign: "left",
  },
  actionListIndex: {
    display: "inline-flex",
    alignItems: "center",
    justifyContent: "center",
    minWidth: 28,
    height: 28,
    borderRadius: "50%",
    background: "#2a7ae2",
    color: "#fff",
    fontWeight: 700,
    fontSize: "0.85rem",
    flexShrink: 0,
  },
  actionListBody: {
    display: "flex",
    flexDirection: "column",
    gap: 2,
    minWidth: 0,
  },
  actionListName: {
    color: "#e0e0e0",
    fontWeight: 600,
    whiteSpace: "nowrap",
    overflow: "hidden",
    textOverflow: "ellipsis",
  },
  actionListMeta: {
    color: "#8aa4be",
    fontSize: "0.75rem",
  },
  actionBtn: {
    marginTop: 4,
    padding: "10px 20px",
    background: "#2a7ae2",
    color: "#fff",
    border: "none",
    borderRadius: 8,
    fontSize: "0.95rem",
    fontWeight: 600,
    cursor: "pointer",
    alignSelf: "flex-start",
  },
  actionBtnSecondary: {
    marginTop: 4,
    padding: "8px 16px",
    background: "transparent",
    color: "#7ec8e3",
    border: "1px solid #2a4a6a",
    borderRadius: 8,
    fontSize: "0.85rem",
    cursor: "pointer",
    alignSelf: "flex-start",
  },
  logToggleBtn: {
    position: "fixed",
    bottom: 20,
    right: 20,
    background: "#222",
    color: "#ccc",
    border: "1px solid #444",
    borderRadius: 8,
    padding: "8px 16px",
    fontSize: "0.85rem",
    cursor: "pointer",
    zIndex: 50,
  },
  logPanel: {
    position: "fixed",
    bottom: 60,
    right: 20,
    // Responsive width: at most 360px but never wider than the viewport minus 40px (20px margin each side)
    width: "min(360px, calc(100vw - 40px))",
    // Responsive height: at most 400px but never taller than viewport minus log-toggle button area (~80px)
    maxHeight: "min(400px, calc(100dvh - 80px))",
    background: "#111",
    borderRadius: 12,
    padding: 16,
    display: "flex",
    flexDirection: "column",
    gap: 10,
    zIndex: 45,
    boxShadow: "0 4px 24px rgba(0,0,0,0.7)",
    border: "1px solid #333",
    overflowY: "auto",
  },
  sectionTitle: {
    fontSize: "0.85rem",
    fontWeight: 600,
    color: "#888",
    textTransform: "uppercase",
    letterSpacing: "0.06em",
    margin: 0,
  },
  emptyLog: { color: "#555", fontSize: "0.875rem", fontStyle: "italic", margin: 0 },
  logList: {
    listStyle: "none",
    margin: 0,
    padding: 0,
    display: "flex",
    flexDirection: "column",
    gap: 6,
  },
  logItem: {
    borderRadius: 6,
    padding: "8px 12px",
    display: "flex",
    gap: 10,
    alignItems: "flex-start",
    fontSize: "0.875rem",
    lineHeight: 1.5,
    color: "#fff",
  },
  logTime: {
    fontSize: "0.72rem",
    color: "rgba(255,255,255,0.5)",
    whiteSpace: "nowrap",
    paddingTop: 2,
    minWidth: 60,
  },
  // Reminders fade-out toast
  remindersToast: {
    position: "fixed",
    top: 72,
    left: "50%",
    transform: "translateX(-50%)",
    background: "#1a2a3a",
    border: "1px solid #2a5a7a",
    borderRadius: 14,
    padding: "18px 24px",
    minWidth: 260,
    maxWidth: 420,
    width: "calc(100% - 48px)",
    boxShadow: "0 8px 32px rgba(0,0,0,0.7)",
    zIndex: 100,
    display: "flex",
    flexDirection: "column",
    gap: 10,
    pointerEvents: "none",   // does not block taps
  },
  remindersToastTitle: {
    margin: 0,
    fontSize: "0.95rem",
    fontWeight: 700,
    color: "#7ec8e3",
    letterSpacing: "0.02em",
  },
  remindersToastEmpty: {
    margin: 0,
    fontSize: "0.9rem",
    color: "#888",
    fontStyle: "italic",
  },
  remindersToastList: {
    listStyle: "none",
    margin: 0,
    padding: 0,
    display: "flex",
    flexDirection: "column",
    gap: 8,
  },
  remindersToastItem: {
    display: "flex",
    alignItems: "baseline",
    gap: 8,
    fontSize: "0.9rem",
    color: "#dde",
    lineHeight: 1.5,
  },
  remindersToastBullet: {
    color: "#7ec8e3",
    fontWeight: 700,
    minWidth: 20,
    flexShrink: 0,
  },
  remindersToastMeta: {
    marginLeft: "auto",
    fontSize: "0.72rem",
    color: "#5a7a8a",
    whiteSpace: "nowrap",
    flexShrink: 0,
  },
};

// Inject pulse keyframe once
if (typeof document !== "undefined" && !document.getElementById("app-pulse-style")) {
  const style = document.createElement("style");
  style.id = "app-pulse-style";
  style.textContent = "@keyframes pulse { 0% { transform: scale(1); } 50% { transform: scale(1.35); } 100% { transform: scale(1); } }";
  document.head.appendChild(style);
}

// Inject spin keyframe once (used by the landing-screen loading spinner)
if (typeof document !== "undefined" && !document.getElementById("app-spin-style")) {
  const style = document.createElement("style");
  style.id = "app-spin-style";
  style.textContent = "@keyframes spin { to { transform: rotate(360deg); } }";
  document.head.appendChild(style);
}

// Inject mobile-responsive rules once.
// On small screens the landing card scales down so all content fits within the viewport.
if (typeof document !== "undefined" && !document.getElementById("app-mobile-style")) {
  const style = document.createElement("style");
  style.id = "app-mobile-style";
  style.textContent = `
    .sgm-landing-cmds { display: none !important; }
    @media (max-width: 600px) {
      .sgm-landing-card-inner { padding: 14px 16px !important; gap: 10px !important; }
      .sgm-landing-title { font-size: 1.25rem !important; }
      .sgm-landing-logo { width: 54px !important; height: 54px !important; }
      .sgm-landing-how { font-size: 0.82rem !important; gap: 6px !important; }
      .sgm-landing-how .sub-line { font-size: 0.74rem !important; }
      .assistant-logo { max-width: 160px !important; }
    }
    @media (max-height: 480px) {
      .assistant-logo { display: none !important; }
    }
  `;
  document.head.appendChild(style);
}
