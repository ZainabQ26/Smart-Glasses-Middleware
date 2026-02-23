import React, { useState, useEffect, useRef, useCallback } from "react";
import { useMicVAD, utils } from "@ricky0123/vad-react";

// ─── HELPERS ──────────────────────────────────────────────────────────────────
function blobToBase64(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => resolve(reader.result.split(",")[1]);
    reader.onerror = reject;
    reader.readAsDataURL(blob);
  });
}

// ─── MAIN COMPONENT ───────────────────────────────────────────────────────────
//
// KEY DESIGN: VAD acquires the mic stream ONCE at startup with startOnLoad:true
// and keeps it open permanently. SpeechRecognition runs in parallel for keyword
// detection — when "activate" fires we just flip a ref; no mic re-acquisition
// happens so there is no race condition.
//
// Flow:
//   [always] SpeechRecognition listening for "activate"
//   "activate" heard → activatedRef=true, rec.abort(), status=listening
//   VAD.onSpeechEnd fires → sees activatedRef=true → sends to Whisper
//   Whisper returns → activatedRef=false, vad.start() resumes, SpeechRecognition restarts
//
export default function App() {
  const [status, setStatus] = useState("idle"); // idle | listening | processing
  const [logs, setLogs] = useState([]);

  // Guard: onSpeechEnd only ships audio when we have been activated.
  // vad.pause/start never touches activatedRef — they just gate VAD processing.
  const activatedRef = useRef(false);
  const recognitionRef = useRef(null);
  // Stable reference to the latest vad object so stale closures never call old handles.
  const vadRef = useRef(null);
  // Timer that auto-resets the listening state if onSpeechEnd never fires.
  const listeningTimeoutRef = useRef(null);

  const addLog = useCallback((text, type = "info") => {
    setLogs(prev =>
      [{ id: Date.now() + Math.random(), text, type, ts: new Date().toLocaleTimeString() }, ...prev].slice(0, 50)
    );
  }, []);

  // ── Silero VAD — owns the mic stream permanently ─────────────────────────
  // startOnLoad:true → getUserMedia is called once on mount and kept open.
  // SpeechRecognition opens its OWN concurrent stream; browsers allow this.
  // When "activate" fires we flip activatedRef; VAD is already running.
  const vad = useMicVAD({
    startOnLoad: false, // Changed from true: only start VAD after activation word
    baseAssetPath: "/", // Silero .onnx model and vad.worklet.bundle.min.js live at wwwroot root
    ortConfig: (ort) => {
      ort.env.wasm.wasmPaths = "/"; // WASM/MJS files are at wwwroot root, not /assets/
      ort.env.wasm.numThreads = 1;  // avoid SharedArrayBuffer requirement as belt-and-suspenders
    },
    onSpeechStart: () => {
      if (activatedRef.current) addLog("🟢 Speech detected…", "info");
    },
    onSpeechEnd: (audio) => {
      if (!activatedRef.current) return; // ignore background speech before activation
      vad.pause();
      setStatus("processing");
      addLog("⏳ Transcribing via Whisper Lambda…", "info");
      // encodeWAV may return ArrayBuffer, Uint8Array, or Blob depending on vad-web version.
      // Normalise to Blob so FileReader.readAsDataURL always gets the right type.
      const wav = utils.encodeWAV(audio);
      const wavBlob = wav instanceof Blob ? wav : new Blob([wav], { type: "audio/wav" });
      sendAudio(wavBlob);
    },
    onVADMisfire: () => {
      if (activatedRef.current) addLog("(Noise ignored, still listening…)", "info");
    },
  });

  vadRef.current = vad;

  // On load, confirm VAD is ready but idle
  useEffect(() => {
    if (!vad.loading && !vad.errored) addLog("✅ Voice detection linked", "info");
  }, [vad.loading]);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (vad.errored) addLog(`❌ VAD error: ${vad.errored.message ?? "ONNX Runtime failed to load"}`, "error");
  }, [vad.errored]);

  // ── SpeechRecognition — keyword-only, runs concurrently with VAD ──────────
  useEffect(() => {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) {
      addLog("⚠️ Wake-word not available — use the mic button below.", "warn");
      return;
    }

    const rec = new SR();
    rec.continuous = true;
    rec.interimResults = true;
    rec.lang = "en-US";

    rec.onresult = (event) => {
      const transcript = Array.from(event.results)
        .map(r => r[0].transcript)
        .join("")
        .toLowerCase();

      if (transcript.includes("activate") && !activatedRef.current) {
        activatedRef.current = true;
        rec.stop(); // Gracefully stop SR stream
        vad.start(); // Start listening for the actual command NOW
        setStatus("listening");
        addLog("🎤 Activated — speak your command", "info");
        // Safety net: reset if onSpeechEnd never fires (VAD error or prolonged silence).
        if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
        listeningTimeoutRef.current = setTimeout(() => {
          if (activatedRef.current) { addLog("⏱️ No speech detected — resetting.", "warn"); resumeListening(); }
        }, 30000);
        // VAD is already started (startOnLoad:true). If it was paused, resume it.
        try { vadRef.current.start(); } catch (_) {}
      }
    };

    rec.onerror = (e) => {
      if (e.error !== "no-speech" && e.error !== "aborted")
        addLog(`Mic error: ${e.error}`, "warn");
    };

    // After abort, restart SR so we keep listening for the next keyword.
    // We only restart when not activated (i.e. the abort was from something
    // other than the keyword flow — e.g. a timeout restart).
    rec.onend = () => {
      if (!activatedRef.current) {
        try { rec.start(); } catch (_) {}
      }
    };

    rec.start();
    recognitionRef.current = rec;
    return () => { try { rec.abort(); } catch (_) {} };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Once transcription is done, resume SR keyword listening.
  const resumeListening = useCallback(() => {
    if (listeningTimeoutRef.current) { clearTimeout(listeningTimeoutRef.current); listeningTimeoutRef.current = null; }
    activatedRef.current = false;
    setStatus("idle");
    try { vadRef.current.start(); } catch (_) {} // resume VAD (was paused during processing)
    try { recognitionRef.current?.start(); } catch (_) {}
  }, []);

  // ── Send WAV to /api/transcribe ──────────────────────────────────────────
  const sendAudio = async (wavBlob) => {
    try {
      const audioBase64 = await blobToBase64(wavBlob);
      const res = await fetch("/api/transcribe", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ audioBase64, mimeType: "audio/wav" }),
      });
      if (!res.ok) throw new Error(`Server ${res.status}: ${await res.text()}`);
      const { transcription } = await res.json();
      const text = transcription?.trim();
      addLog(text ? `🗣️ "${text}"` : "(No speech detected)", text ? "voice" : "warn");
    } catch (err) {
      addLog(`❌ ${err.message}`, "error");
    } finally {
      resumeListening();
    }
  };

  // ── Manual tap button (fallback / supplement to wake-word) ───────────────
  const handleMicButton = () => {
    if (status === "idle") {
      activatedRef.current = true;
      try { vad.start(); } catch (_) {}
      setStatus("listening");
      addLog("🎤 Listening… speak your command", "info");
      if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
      listeningTimeoutRef.current = setTimeout(() => {
        if (activatedRef.current) { addLog("⏱️ No speech detected — resetting.", "warn"); resumeListening(); }
      }, 30000);
      try { vadRef.current.start(); } catch (_) {}
    } else if (status === "listening") {
      if (listeningTimeoutRef.current) { clearTimeout(listeningTimeoutRef.current); listeningTimeoutRef.current = null; }
      activatedRef.current = false;
      try { vad.pause(); } catch (_) {}
      setStatus("idle");
      addLog("Cancelled.", "info");
      try { recognitionRef.current?.start(); } catch (_) {}
    }
  };

  // ── Render ────────────────────────────────────────────────────────────────
  const isListening = status === "listening";
  const isProcessing = status === "processing";
  const btnLabel = isProcessing
    ? "⏳ Transcribing…"
    : isListening
      ? (vad.userSpeaking ? "🗣️ Speaking…" : "🎤 Listening — tap to cancel")
      : vad.loading
        ? "⏱️ Loading voice detection…"
        : vad.errored
          ? '⚠️ VAD error — tap mic to retry'
          : 'Say "Activate" or tap 🎤';
  const btnColor = isProcessing ? "#6c757d"
    : isListening ? "#e67e22"
    : vad.errored ? "#e74c3c"
    : "#00b894";

  return (
    <div style={styles.page}>
      <div style={styles.indicator}>
        <span style={{ ...styles.dot, background: btnColor }} />
        <span style={styles.indicatorText}>{btnLabel}</span>
      </div>

      <main style={styles.main}>
        <div style={styles.btnWrap}>
          <button
            onClick={handleMicButton}
            disabled={isProcessing}
            style={{ ...styles.micBtn, background: btnColor, opacity: isProcessing ? 0.6 : 1 }}
          >
            {isProcessing ? "⏳" : isListening ? "⏹" : "🎤"}
          </button>
          <p style={styles.btnHint}>{btnLabel}</p>
        </div>

        <section style={{ ...styles.card, flex: 1 }}>
          <h2 style={styles.sectionTitle}>Activity Log</h2>
          {logs.length === 0 ? (
            <p style={styles.emptyLog}>No activity yet. Say "Activate" or tap the mic.</p>
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
        </section>
      </main>
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
  card: {
    background: "#111",
    borderRadius: 12,
    padding: 20,
    display: "flex",
    flexDirection: "column",
    gap: 10,
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
    maxHeight: 400,
    overflowY: "auto",
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
