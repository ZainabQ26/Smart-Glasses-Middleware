import React, { useState, useCallback } from "react";
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
// The VAD owns the mic exclusively — no SpeechRecognition, no mic conflict.
// Flow: tap button → vad.start() → Silero VAD fires onSpeechEnd →
//       encodeWAV → POST /api/transcribe → result logged → back to idle.
export default function App() {
  const [status, setStatus] = useState("idle"); // idle | listening | processing
  const [logs, setLogs] = useState([]);

  const addLog = useCallback((text, type = "info") => {
    setLogs(prev =>
      [{ id: Date.now() + Math.random(), text, type, ts: new Date().toLocaleTimeString() }, ...prev].slice(0, 50)
    );
  }, []);

  // ── Silero VAD ───────────────────────────────────────────────────────────
  const vad = useMicVAD({
    startOnLoad: false,
    baseAssetPath: "/",
    ortConfig: (ort) => { ort.env.wasm.numThreads = 1; },
    onSpeechStart: () => {
      addLog("🟢 Speech detected…", "info");
    },
    onSpeechEnd: (audio) => {
      vad.pause();
      setStatus("processing");
      addLog("⏳ Transcribing via Whisper Lambda…", "info");
      sendAudio(utils.encodeWAV(audio));
    },
    onVADMisfire: () => {
      addLog("(Noise ignored, still listening…)", "info");
    },
  });

  // ── Tap-to-listen toggle ─────────────────────────────────────────────────
  const handleMicButton = () => {
    if (status === "idle") {
      setStatus("listening");
      addLog("🎤 Listening… speak now", "info");
      vad.start();
    } else if (status === "listening") {
      vad.pause();
      setStatus("idle");
      addLog("Cancelled.", "info");
    }
    // processing: button disabled
  };

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
      setStatus("idle");
    }
  };

  // ── Render ────────────────────────────────────────────────────────────────
  const isListening = status === "listening";
  const isProcessing = status === "processing";
  const btnLabel = isProcessing
    ? "⏳ Transcribing…"
    : isListening
      ? (vad.userSpeaking ? "🗣️ Speaking…" : "🎤 Listening — tap to cancel")
      : "🎤 Tap to Listen";
  const btnColor = isProcessing ? "#6c757d" : isListening ? "#e67e22" : "#00b894";

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
            <p style={styles.emptyLog}>No activity yet. Tap the mic to start.</p>
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
