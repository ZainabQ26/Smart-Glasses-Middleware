import React, { useState, useEffect, useRef, useCallback } from "react";
import { useMicVAD } from "@ricky0123/vad-react";
import WhisperWorker from "./whisperWorker?worker";

// ─── MAIN COMPONENT ───────────────────────────────────────────────────────────
//
// VAD: Silero VAD v5 — the same neural model used by whisper.cpp internally.
//      Shipped by @ricky0123/vad-web; ONNX files are copied to wwwroot at build
//      time (see vite.config.js).  Runs entirely in the browser.
//
// Flow:
//   [always] SpeechRecognition listening for "activate"
//   "activate" heard → SpeechRecognition captures command (primary path)
//   If Web Speech confidence is low → fallback to Whisper worker result
//
export default function App() {
  const [status, setStatus]       = useState("idle"); // idle | listening | processing | sending
  const [logs, setLogs]           = useState([]);
  const [transcript, setTranscript] = useState(null);
  const [aiResponse, setAiResponse] = useState(null);
  const [usedAdvancedModel, setUsedAdvancedModel] = useState(false);
  const [speechMode, setSpeechMode] = useState("auto"); // auto | webspeech-only | whisper-only
  const [pendingAction, setPendingAction] = useState(null); // { type: "OPEN_FILE_PICKER" } | { type: "SELECT_DOCUMENT", id, name } | null
  const [logOpen, setLogOpen]     = useState(false);

  const activatedRef        = useRef(false);
  const recognitionRef      = useRef(null);
  const vadRef              = useRef(null);   // stable handle — avoids stale closures
  const listeningTimeoutRef = useRef(null);
  const workerRef           = useRef(null);
  const sendToAIRef         = useRef(null);
  const fileInputRef        = useRef(null);
  const whisperFallbackNeededRef = useRef(false);
  const webSpeechHandledRef      = useRef(false);
  const lowConfidenceTranscriptRef = useRef(null);

  const WEB_SPEECH_CONFIDENCE_THRESHOLD = 0.72;
  const isWebSpeechOnly = speechMode === "webspeech-only";
  const isWhisperOnly = speechMode === "whisper-only";

  const addLog = useCallback((text, type = "info") => {
    setLogs(prev =>
      [{ id: Date.now() + Math.random(), text, type, ts: new Date().toLocaleTimeString() }, ...prev].slice(0, 50)
    );
  }, []);

  useEffect(() => {
    let cancelled = false;
    const loadClientSettings = async () => {
      try {
        const res = await fetch("/api/process/client-settings");
        if (!res.ok) return;
        const data = await res.json();
        const mode = (data?.speechTranscriptionMode || "").toLowerCase();
        const normalized = mode === "webspeech-only" || mode === "whisper-only" ? mode : "auto";
        if (!cancelled) {
          setSpeechMode(normalized);
          addLog(`⚙️ Speech mode: ${normalized}`, "info");
        }
      } catch (_) {
      }
    };

    loadClientSettings();
    return () => { cancelled = true; };
  }, [addLog]);

  // ── Silero VAD (whisper.cpp model) ────────────────────────────────────────
  // startOnLoad:false → mic is only acquired after the wake word.
  // The Silero v5 .onnx model and worklet bundle are served from wwwroot root
  // (copied there by viteStaticCopy in vite.config.js).
  const vad = useMicVAD({
    startOnLoad: false,
    baseAssetPath: "/",
    ortConfig: (ort) => {
      ort.env.wasm.wasmPaths = "/";
      ort.env.wasm.numThreads = 1; // no SharedArrayBuffer/COOP-COEP headers needed
    },
    onSpeechStart: () => {
      if (activatedRef.current) addLog("🟢 Speech detected…", "info");
    },
    onSpeechEnd: (audio) => {
      // `audio` is a 16 kHz mono Float32Array — exactly what Whisper expects.
      if (!activatedRef.current) return;
      if (isWebSpeechOnly) return;
      if (listeningTimeoutRef.current) {
        clearTimeout(listeningTimeoutRef.current);
        listeningTimeoutRef.current = null;
      }
      vadRef.current?.pause();
      addLog("⏳ Whisper fallback transcript preparing…", "info");
      // Transfer the buffer zero-copy; worker owns it from here.
      workerRef.current?.postMessage({ type: "transcribe", audio }, [audio.buffer]);
    },
    onVADMisfire: () => {
      if (activatedRef.current) addLog("(Noise ignored, still listening…)", "info");
    },
  });

  // Keep vadRef in sync so callbacks always call the current instance.
  vadRef.current = vad;

  useEffect(() => {
    if (!vad.loading && !vad.errored) addLog("✅ Silero VAD ready", "info");
  }, [vad.loading]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (vad.errored) addLog(`❌ VAD error: ${vad.errored.message ?? "Silero model failed to load"}`, "error");
  }, [vad.errored]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Resume SR keyword listening ───────────────────────────────────────────
  // Delay before restarting the mic on iOS Safari: gives the browser GC time
  // to reclaim the ONNX/WASM memory freed by the Whisper worker before a new
  // audio session is opened, preventing the "content process terminated" /
  // page-refresh crash that iOS triggers under memory pressure.
  // 300 ms is enough for one GC cycle while being imperceptible to the user.
  const RESUME_DELAY_MS = 300;
  const resumeListening = useCallback(() => {
    if (listeningTimeoutRef.current) { clearTimeout(listeningTimeoutRef.current); listeningTimeoutRef.current = null; }
    activatedRef.current = false;
    whisperFallbackNeededRef.current = false;
    webSpeechHandledRef.current = false;
    lowConfidenceTranscriptRef.current = null;
    setStatus("idle");
    try { vadRef.current?.pause(); } catch (_) {}
    setTimeout(() => { try { recognitionRef.current?.start(); } catch (_) {} }, RESUME_DELAY_MS);
  }, [RESUME_DELAY_MS]);

  const resumeListeningRef = useRef(resumeListening);
  useEffect(() => { resumeListeningRef.current = resumeListening; }, [resumeListening]);

  // ── Send transcript to AI ─────────────────────────────────────────────────
  const sendToAI = useCallback(async (text) => {
    try {
      // Preview whether the backend will delegate this request to the advanced model
      // Clear previous state first to avoid stale badge persisting on preview error
      setUsedAdvancedModel(false);
      try {
        const previewRes = await fetch('/api/process/preview', {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ input: text })
        });
        if (previewRes.ok) {
          const previewJson = await previewRes.json();
          const willUse = !!previewJson?.willUseAdvanced;
          setUsedAdvancedModel(willUse);
        } else {
          setUsedAdvancedModel(false);
        }
      } catch (previewErr) {
        // On preview failure, clear badge to avoid misleading UI
        setUsedAdvancedModel(false);
      }
      addLog("🤖 Sending to AI…", "info");
      const res = await fetch("/api/process", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ input: text }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      const answer = (data.response || "").trim();
      const usedAdvanced = data?.usedAdvancedModel ?? data?.UsedAdvancedModel ?? data?.usedAdvanced ?? false;

      // ── Action token interception ────────────────────────────────────────
      if (answer === "__ACTION:OPEN_FILE_PICKER__") {
        addLog("📂 Command: open file picker", "voice");
        setAiResponse(null);
        setPendingAction({ type: "OPEN_FILE_PICKER" });
      } else if (answer.startsWith("__ACTION:SELECT_DOCUMENT:")) {
        const parts = answer.split(":");
        const docId   = parts[2];
        const docName = parts.slice(3).join(":");
        addLog(`📄 Command: select document "${docName}"`, "voice");
        setAiResponse(null);
        setPendingAction({ type: "SELECT_DOCUMENT", id: docId, name: docName });
      } else {
        addLog(`💡 AI: "${answer}"`, "voice");
        setPendingAction(null);
        setAiResponse(answer);
        setUsedAdvancedModel(usedAdvanced);
      }
    } catch (err) {
      addLog(`❌ AI error: ${err.message}`, "error");
      setAiResponse(null);
      setPendingAction(null);
    } finally {
      resumeListeningRef.current();
    }
  }, [addLog]);

  sendToAIRef.current = sendToAI;
  useEffect(() => { sendToAIRef.current = sendToAI; }, [sendToAI]);

  // ── SpeechRecognition — "activate" keyword only ───────────────────────────
  useEffect(() => {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) {
      addLog("⚠️ Wake-word not available — use the mic button below.", "warn");
      return;
    }

    const rec = new SR();
    rec.continuous     = true;
    rec.interimResults = true;
    rec.lang           = "en-US";

    rec.onresult = (event) => {
      const transcript = Array.from(event.results)
        .map(r => r[0].transcript)
        .join("")
        .toLowerCase();

      const lastResult = event.results[event.results.length - 1];
      const lastAlt = lastResult?.[0];
      const latestText = (lastAlt?.transcript || "").trim().toLowerCase();
      const confidence = typeof lastAlt?.confidence === "number" ? lastAlt.confidence : null;

      if (transcript.includes("activate") && !activatedRef.current) {
        activatedRef.current = true;
        webSpeechHandledRef.current = false;
        whisperFallbackNeededRef.current = false;
        lowConfidenceTranscriptRef.current = null;
        setStatus("listening");
        addLog("🎤 Activated — speak your command", "info");

        if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
        listeningTimeoutRef.current = setTimeout(() => {
          if (activatedRef.current) { addLog("⏱️ No speech detected — resetting.", "warn"); resumeListeningRef.current(); }
        }, 30_000);

        if (!isWebSpeechOnly) {
          try { vadRef.current.start(); } catch (_) {}
        }
        return;
      }

      if (!activatedRef.current) return;
      if (webSpeechHandledRef.current) return;
      if (!lastResult?.isFinal) return;
      if (!latestText) return;

      if (listeningTimeoutRef.current) {
        clearTimeout(listeningTimeoutRef.current);
        listeningTimeoutRef.current = null;
      }

      if (isWhisperOnly) {
        whisperFallbackNeededRef.current = true;
        lowConfidenceTranscriptRef.current = latestText;
        setStatus("processing");
        addLog("⚙️ Whisper-only mode: using Whisper transcript.", "info");
        return;
      }

      if (confidence != null && confidence < WEB_SPEECH_CONFIDENCE_THRESHOLD && !isWebSpeechOnly) {
        whisperFallbackNeededRef.current = true;
        lowConfidenceTranscriptRef.current = latestText;
        setStatus("processing");
        addLog(`⚠️ Web Speech confidence ${confidence.toFixed(2)} is low — using Whisper fallback.`, "warn");
        return;
      }

      whisperFallbackNeededRef.current = false;
      lowConfidenceTranscriptRef.current = null;
      webSpeechHandledRef.current = true;
      addLog(confidence != null
        ? `🗣️ Web Speech (${confidence.toFixed(2)}): "${latestText}"`
        : `🗣️ Web Speech: "${latestText}"`, "voice");
      setTranscript(latestText);
      setAiResponse(null);
      setPendingAction(null);
      setStatus("sending");
      sendToAIRef.current?.(latestText);
    };

    rec.onerror = (e) => {
      if (e.error !== "no-speech" && e.error !== "aborted")
        addLog(`Mic error: ${e.error}`, "warn");
    };

    rec.onend = () => {
      if (!activatedRef.current) { try { rec.start(); } catch (_) {} }
    };

    rec.start();
    recognitionRef.current = rec;
    return () => { try { rec.abort(); } catch (_) {} };
  }, [isWebSpeechOnly, isWhisperOnly]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Whisper Web Worker ────────────────────────────────────────────────────
  useEffect(() => {
    const worker = new WhisperWorker();
    workerRef.current = worker;

    worker.onmessage = (e) => {
      const { type, text, message } = e.data;
      if (type === "loading") {
        addLog(`⏳ ${message}`, "info");
      } else if (type === "result") {
        const cleaned = (text || "").trim();
        const meaningful = cleaned.length >= 2 && /[A-Za-z0-9]/.test(cleaned);
        if (meaningful) {
          if (isWebSpeechOnly) return;
          const shouldUseWhisper = whisperFallbackNeededRef.current || !webSpeechHandledRef.current;
          if (!shouldUseWhisper) return;

          webSpeechHandledRef.current = true;
          whisperFallbackNeededRef.current = false;
          lowConfidenceTranscriptRef.current = null;
          addLog(`🗣️ Whisper: "${cleaned}"`, "voice");
          setTranscript(cleaned);
          setAiResponse(null);
          setPendingAction(null);
          setStatus("sending");
          sendToAIRef.current?.(cleaned);
        } else {
          if (whisperFallbackNeededRef.current && lowConfidenceTranscriptRef.current && !webSpeechHandledRef.current) {
            const fallbackText = lowConfidenceTranscriptRef.current;
            webSpeechHandledRef.current = true;
            whisperFallbackNeededRef.current = false;
            lowConfidenceTranscriptRef.current = null;
            addLog("⚠️ Whisper returned no speech — using low-confidence Web Speech transcript.", "warn");
            addLog(`🗣️ Web Speech fallback: "${fallbackText}"`, "voice");
            setTranscript(fallbackText);
            setAiResponse(null);
            setPendingAction(null);
            setStatus("sending");
            sendToAIRef.current?.(fallbackText);
            return;
          }
          addLog("(No speech detected)", "warn");
          resumeListeningRef.current();
        }
      } else if (type === "error") {
        if (isWebSpeechOnly) {
          addLog("❌ Whisper error (ignored in webspeech-only mode)", "warn");
          return;
        }
        if (whisperFallbackNeededRef.current && lowConfidenceTranscriptRef.current && !webSpeechHandledRef.current) {
          const fallbackText = lowConfidenceTranscriptRef.current;
          webSpeechHandledRef.current = true;
          whisperFallbackNeededRef.current = false;
          lowConfidenceTranscriptRef.current = null;
          addLog("⚠️ Whisper error — using low-confidence Web Speech transcript.", "warn");
          addLog(`🗣️ Web Speech fallback: "${fallbackText}"`, "voice");
          setTranscript(fallbackText);
          setAiResponse(null);
          setPendingAction(null);
          setStatus("sending");
          sendToAIRef.current?.(fallbackText);
          return;
        }
        addLog(`❌ Whisper: ${message}`, "error");
        resumeListeningRef.current();
      }
    };

    worker.onerror = (err) => {
      // err is an ErrorEvent; .message is sometimes empty so fall back to other fields
      const msg = err.message || (err.filename ? `${err.filename}:${err.lineno}` : err.type) || "unknown error";
      if (whisperFallbackNeededRef.current && lowConfidenceTranscriptRef.current && !webSpeechHandledRef.current) {
        const fallbackText = lowConfidenceTranscriptRef.current;
        webSpeechHandledRef.current = true;
        whisperFallbackNeededRef.current = false;
        lowConfidenceTranscriptRef.current = null;
        addLog("⚠️ Whisper worker crashed — using low-confidence Web Speech transcript.", "warn");
        addLog(`🗣️ Web Speech fallback: "${fallbackText}"`, "voice");
        setTranscript(fallbackText);
        setAiResponse(null);
        setPendingAction(null);
        setStatus("sending");
        sendToAIRef.current?.(fallbackText);
        return;
      }
      addLog(`❌ Whisper worker crashed: ${msg}`, "error");
      resumeListeningRef.current();
    };

    return () => worker.terminate();
  }, [isWebSpeechOnly]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    return () => {
      if (listeningTimeoutRef.current) {
        clearTimeout(listeningTimeoutRef.current);
        listeningTimeoutRef.current = null;
      }
    };
  }, []);

  // ── Manual tap button ─────────────────────────────────────────────────────
  const handleMicButton = () => {
    if (status === "idle") {
      activatedRef.current = true;
      setStatus("listening");
      setTranscript(null);
      setAiResponse(null);
      addLog("🎤 Listening… speak your command", "info");
      if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
      listeningTimeoutRef.current = setTimeout(() => {
        if (activatedRef.current) { addLog("⏱️ No speech detected — resetting.", "warn"); resumeListening(); }
      }, 30_000);
      if (!isWebSpeechOnly) {
        try { vadRef.current.start(); } catch (_) {}
      }
    } else if (status === "listening") {
      if (listeningTimeoutRef.current) { clearTimeout(listeningTimeoutRef.current); listeningTimeoutRef.current = null; }
      activatedRef.current = false;
      try { vadRef.current?.pause(); } catch (_) {}
      setStatus("idle");
      addLog("Cancelled.", "info");
      try { recognitionRef.current?.start(); } catch (_) {}
    }
  };

  // ── Render ────────────────────────────────────────────────────────────────
  const isListening  = status === "listening";
  const isProcessing = status === "processing";
  const isSending    = status === "sending";
  const btnLabel = isSending
    ? "🤖 Sending to AI…"
    : isProcessing
      ? "⏳ Transcribing…"
      : isListening
        ? (vad.userSpeaking ? "🗣️ Speaking…" : "🎤 Listening")
        : vad.loading
          ? "⏱️ Loading Silero VAD…"
          : vad.errored
            ? "⚠️ VAD error — say Activate to retry"
            : 'Say "Activate"';
  const dotColor = isSending    ? "#3498db"
    : isProcessing ? "#9b59b6"
    : isListening  ? "#e67e22"
    : vad.errored  ? "#e74c3c"
    : "#00b894";

  const shouldPulse = vad.loading || isProcessing || isSending;
  const dotStyle = { ...styles.dot, background: dotColor, ...(shouldPulse ? { animation: "pulse 1s ease-in-out infinite" } : {}) };

  return (
    <div style={styles.page}>
      {/* Hidden file input — triggered by tap after "open file" command */}
      <input
        ref={fileInputRef}
        type="file"
        style={{ display: "none" }}
        onChange={(e) => {
          const file = e.target.files?.[0];
          if (file) addLog(`📎 File selected: ${file.name}`, "info");
          setPendingAction(null);
          e.target.value = "";
        }}
      />

      <div style={styles.indicator}>
        <span style={dotStyle} />
        <span style={styles.indicatorText}>{btnLabel}</span>
        {usedAdvancedModel && (
          <div title="Advanced model selected" style={{display:'inline-flex', alignItems:'center', gap:6, marginLeft:6}}>
            <span style={{fontSize:14, color:'#7ec8e3'}}>⚡</span>
          </div>
        )}
      </div>

      <main style={styles.main}>

        {(transcript || isSending) && (
          <section style={styles.conversationArea}>
            {transcript && (
              <div style={styles.transcriptBubble}>
                <span style={styles.bubbleLabel}>You</span>
                <p style={styles.bubbleText}>{transcript}</p>
              </div>
            )}
            {isSending && !aiResponse && !pendingAction && (
              <div style={styles.thinkingBubble}>
                <span style={styles.thinkingText}>AI is thinking…</span>
              </div>
            )}
            {aiResponse && !pendingAction && (
              <div style={styles.aiBubble}>
                <span style={styles.bubbleLabel}>AI</span>
                <p style={styles.bubbleText}>{aiResponse}</p>
                {usedAdvancedModel && (
                  <div style={{position: 'absolute', right: 12, bottom: 8, background: 'rgba(11,99,166,0.9)', color: 'white', padding: '4px 6px', borderRadius: 6, fontSize: 12, display: 'flex', alignItems: 'center', gap: 6}} title="Generated by advanced model">
                    <span style={{fontSize:12}}>⚡</span>
                    <span style={{fontSize:11}}>Advanced</span>
                  </div>
                )}
              </div>
            )}
            {pendingAction?.type === "OPEN_FILE_PICKER" && (
              <div style={styles.actionCard}>
                <p style={styles.actionCardTitle}>📂 Open File</p>
                <p style={styles.actionCardHint}>Tap the button below to open the file picker</p>
                <button
                  style={styles.actionBtn}
                  onClick={() => { fileInputRef.current?.click(); }}
                >
                  Browse Files
                </button>
              </div>
            )}
            {pendingAction?.type === "SELECT_DOCUMENT" && (
              <div style={styles.actionCard}>
                <p style={styles.actionCardTitle}>📄 Last Uploaded Document</p>
                <p style={styles.actionCardDoc}>{pendingAction.name}</p>
                <button
                  style={styles.actionBtnSecondary}
                  on Click={() => {
                    window.open(`/admin.html?preview=${pendingAction.id}&name=${encodeURIComponent(pendingAction.name)}`, "_blank");
                    setPendingAction(null);
                  }}
                >
                  View in Admin Panel →
                </button>
              </div>
            )}
          </section>
        )}
      </main>

      <button
        style={styles.logToggleBtn}
        onClick={() => setLogOpen(prev => !prev)}
      >
        {logOpen ? "✕ Close Log" : "📋 Activity Log"}
      </button>

      {logOpen && (
        <div style={styles.logPanel}>
          <h2 style={styles.sectionTitle}>Activity Log</h2>
          {logs.length === 0 ? (
            <p style={styles.emptyLog}>No activity yet. Say "Activate".</p>
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

function logBg(type) {
  return { voice: "#00695c", doc: "#1565c0", error: "#b71c1c", warn: "#e65100" }[type] ?? "#333";
}

const styles = {
  page: {
    minHeight: "100vh",
    background: "#000",
    color: "#e0e0e0",
    display: "flex",
    flexDirection: "column",
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
    position: "relative",
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
    maxWidth: 680,
    width: "100%",
    margin: "48px auto 32px",
    padding: "0 16px",
    display: "flex",
    flexDirection: "column",
    gap: 24,
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
    width: 360,
    maxHeight: 400,
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
};

// Inject pulse keyframe once
if (typeof document !== "undefined" && !document.getElementById("app-pulse-style")) {
  const style = document.createElement("style");
  style.id = "app-pulse-style";
  style.textContent = "@keyframes pulse { 0% { transform: scale(1); } 50% { transform: scale(1.35); } 100% { transform: scale(1); } }";
  document.head.appendChild(style);
}
