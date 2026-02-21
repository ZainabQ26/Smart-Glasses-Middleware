import React, { useState, useEffect, useRef, useCallback } from 'react';

// ─── CONSTANTS ────────────────────────────────────────────────────────────────
const MIC_RELEASE_DELAY_MS  = 250;   // Give SpeechRecognition time to release before getUserMedia
// Time-domain RMS: values represent peak deviation from 128 (silence = 0, max speech ~20-40).
// Frequency-domain was unreliable (background noise spikes); time-domain is more accurate.
const SILENCE_THRESHOLD     = 8;     // RMS amplitude below which we consider it silence
const SILENCE_DURATION_MS   = 1500;  // Stop after this many ms of consecutive silence
const MIN_RECORD_MS         = 1000;  // Always capture at least this much audio
const MAX_RECORD_MS         = 30000; // Hard cap to avoid indefinite recording

// ─── HELPERS ─────────────────────────────────────────────────────────────────
function blobToBase64(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      // result is "data:<mime>;base64,<data>" — strip the prefix
      const base64 = reader.result.split(',')[1];
      resolve(base64);
    };
    reader.onerror = reject;
    reader.readAsDataURL(blob);
  });
}

// ─── MAIN COMPONENT ──────────────────────────────────────────────────────────
export default function App() {
  const [status, setStatus] = useState('idle');        // idle | listening | processing | uploading
  const [logs, setLogs] = useState([]);
  const [docStatus, setDocStatus] = useState('ready'); // ready | uploading | done | error

  const recognitionRef = useRef(null);
  const mediaRecorderRef = useRef(null);
  const streamRef = useRef(null);       // active getUserMedia stream — released on unmount/navigation
  const audioChunksRef = useRef([]);
  const fileInputRef = useRef(null);
  const activatingRef = useRef(false); // guard against concurrent activations

  // ── Append an entry to the log list ────────────────────────────────────────
  const addLog = useCallback((text, type = 'info') => {
    setLogs(prev => [{ id: Date.now() + Math.random(), text, type, ts: new Date().toLocaleTimeString() }, ...prev].slice(0, 50));
  }, []);

  // ── Speech recognition (wake-word listener) ───────────────────────────────
  // Initialise the SpeechRecognition object once, but don't start it yet.
  useEffect(() => {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) return; // handled in the toggle handler

    const rec = new SR();
    rec.continuous = true;
    rec.interimResults = true;
    rec.lang = 'en-US';

    rec.onresult = (event) => {
      const transcript = Array.from(event.results)
        .map(r => r[0].transcript)
        .join('')
        .toLowerCase();

      if (transcript.includes('activate') && !activatingRef.current) {
        activatingRef.current = true;
        rec.stop();
        startRecording();
      }
    };

    rec.onerror = (e) => {
      if (e.error !== 'no-speech' && e.error !== 'aborted')
        addLog(`Mic error: ${e.error}`, 'warn');
    };

    recognitionRef.current = rec;
    // abort() immediately kills the underlying audio capture;
    // stop() only ends recognition but the browser keeps the mic indicator lit.
    return () => {
      try { rec.abort(); } catch (_) {}
      streamRef.current?.getTracks().forEach(t => t.stop());
      streamRef.current = null;
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Start/stop the wake-word listener when we go idle.
  // admin.html and monitoring.html are separate HTML files — they never load
  // this component and never acquire the microphone.
  useEffect(() => {
    const rec = recognitionRef.current;
    if (!rec) return;
    if (status === 'idle') {
      try { rec.start(); } catch (_) {}
    } else {
      try { rec.abort(); } catch (_) {}
    }
  }, [status]);

  // ── Recording (silence-detection based) ──────────────────────────────────
  const startRecording = async () => {
    setStatus('listening');
    addLog('🎤 Listening… speak your command', 'info');

    // Brief pause so SpeechRecognition fully releases the mic before getUserMedia.
    await new Promise(r => setTimeout(r, MIC_RELEASE_DELAY_MS));

    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (err) {
      addLog(`Microphone access denied: ${err.message}`, 'error');
      activatingRef.current = false;
      setStatus('idle');
      return;
    }
    streamRef.current = stream;

    // ── Silence detection via Web Audio API ────────────────────────────────
    const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
    const source   = audioCtx.createMediaStreamSource(stream);
    const analyser = audioCtx.createAnalyser();
    // fftSize 2048 gives 2048 time-domain samples — good frequency resolution isn't needed here.
    analyser.fftSize = 2048;
    source.connect(analyser);
    // Use time-domain data: each sample is 0-255 where 128 = silence.
    // RMS of (sample - 128) gives true amplitude regardless of background noise baseline.
    const timeDomainData = new Uint8Array(analyser.fftSize);

    const startTime    = Date.now();
    let lastSoundTime  = Date.now();
    let spokenOnce     = false;

    // Use the browser's native MIME type so ffmpeg/Whisper can decode it.
    const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
      ? 'audio/webm;codecs=opus'
      : MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm' : '';
    const recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
    mediaRecorderRef.current = recorder;
    audioChunksRef.current = [];

    // Poll audio levels every 100 ms.
    const pollInterval = setInterval(() => {
      analyser.getByteTimeDomainData(timeDomainData);
      // Compute RMS of deviation from centre (128 = silence).
      const rms = Math.sqrt(timeDomainData.reduce((s, v) => s + (v - 128) ** 2, 0) / timeDomainData.length);
      const elapsed = Date.now() - startTime;

      if (rms > SILENCE_THRESHOLD) {
        lastSoundTime = Date.now();
        if (!spokenOnce) {
          spokenOnce = true;
          addLog('🟢 Detected speech…', 'info');
        }
      }

      const silent   = Date.now() - lastSoundTime > SILENCE_DURATION_MS;
      const minMet   = elapsed >= MIN_RECORD_MS;
      const maxHit   = elapsed >= MAX_RECORD_MS;

      if ((silent && spokenOnce && minMet) || maxHit) {
        clearInterval(pollInterval);
        if (recorder.state === 'recording') recorder.stop();
      }
    }, 100);

    recorder.ondataavailable = (e) => {
      if (e.data.size > 0) audioChunksRef.current.push(e.data);
    };

    recorder.onstop = async () => {
      clearInterval(pollInterval);
      audioCtx.close();
      stream.getTracks().forEach(t => t.stop());
      streamRef.current = null;
      const audioBlob = new Blob(audioChunksRef.current, { type: recorder.mimeType || 'audio/webm' });
      await sendAudioToBackend(audioBlob, recorder.mimeType);
    };

    recorder.start();
  };

  // ── Send audio to /api/transcribe (Whisper Lambda on-demand) ─────────────
  const sendAudioToBackend = async (audioBlob, mimeType) => {
    setStatus('processing');
    addLog('⏳ Transcribing via Whisper Lambda…', 'info');

    try {
      const audioBase64 = await blobToBase64(audioBlob);

      const res = await fetch('/api/transcribe', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ audioBase64, mimeType: mimeType || 'audio/webm' }),
      });

      if (!res.ok) {
        const err = await res.text();
        throw new Error(`Server ${res.status}: ${err}`);
      }

      const data = await res.json();
      const text = data.transcription?.trim();

      if (text) {
        addLog(`🗣️ "${text}"`, 'voice');
      } else {
        addLog('(No speech detected)', 'warn');
      }
    } catch (err) {
      addLog(`❌ Transcription failed: ${err.message}`, 'error');
    } finally {
      activatingRef.current = false;
      setStatus('idle');
    }
  };

  // ── Document upload — same pipeline as the Admin page (/api/documents/upload)
  const handleFileSelect = async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;
    event.target.value = ''; // reset so the same file can be re-selected

    setDocStatus('uploading');
    addLog(`📤 Uploading ${file.name}…`, 'info');

    const formData = new FormData();
    formData.append('file', file);

    try {
      const res = await fetch('/api/documents/upload', {
        method: 'POST',
        body: formData,
      });

      if (!res.ok) {
        const errText = await res.text();
        throw new Error(`Server ${res.status}: ${errText}`);
      }

      const data = await res.json();
      addLog(`📄 Document queued for processing: ${file.name}`, 'doc');
      setDocStatus('done');
      setTimeout(() => setDocStatus('ready'), 3000);
    } catch (err) {
      addLog(`❌ Upload failed: ${err.message}`, 'error');
      setDocStatus('error');
      setTimeout(() => setDocStatus('ready'), 4000);
    }
  };

  // ── Status label helpers ──────────────────────────────────────────────────
  const statusLabel = {
    idle: 'Say "Activate"',
    listening: '🎤 Recording…',
    processing: '⏳ Transcribing…',
    uploading: '📤 Uploading…',
  }[status] ?? status;

  const statusColor = status === 'idle' ? '#28a745' : '#e67e22';

  const docLabel = {
    ready: '📁 Select Document',
    uploading: 'Uploading…',
    done: 'Uploaded ✅',
    error: 'Upload Failed ❌',
  }[docStatus] ?? docStatus;

  const docColor = {
    ready: '#007bff',
    uploading: '#6c757d',
    done: '#28a745',
    error: '#dc3545',
  }[docStatus] ?? '#007bff';

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div style={styles.page}>
      <div style={styles.voiceIndicator}>
        <span style={{ ...styles.statusDot, background: statusColor }} />
        <div style={styles.statusText}>{statusLabel}</div>
      </div>

      <main style={styles.main}>

        {/* Voice control card removed; top-right indicator remains */}

        {/* Document upload removed from home page */}

        {/* Activity log */}
        <section style={{ ...styles.card, flex: 1, background: '#000' }}>
          <h2 style={styles.sectionTitle}>Activity Log</h2>
          {logs.length === 0 ? (
            <p style={{ ...styles.emptyLog, color: '#aaa' }}>No activity yet. Say "Activate".</p>
          ) : (
            <ul style={styles.logList}>
              {logs.map(entry => (
                <li key={entry.id} style={{ ...styles.logItem, background: logColor(entry.type), color: '#fff', borderLeft: 'none' }}>
                  <span style={{ ...styles.logTime, color: '#fff' }}>{entry.ts}</span>
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

function logColor(type) {
  switch (type) {
    case 'voice': return '#00e676';
    case 'doc':   return '#42a5f5';
    case 'error': return '#ef5350';
    case 'warn':  return '#ffa726';
    default:      return '#555';
  }
}

// ─── STYLES (inline to keep the component self-contained) ─────────────────────
const styles = {
  page: {
    minHeight: '100vh',
    position: 'relative',
    background: '#000000',
    color: '#e0e0e0',
    display: 'flex',
    flexDirection: 'column',
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
  },
  header: {
    background: '#2c3e50',
    padding: '16px 32px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    flexWrap: 'wrap',
    gap: 12,
  },
  title: {
    fontSize: '1.4rem',
    color: '#fff',
    fontWeight: 600,
  },
  nav: {
    display: 'flex',
    gap: 8,
  },
  navLink: {
    color: '#2c3e50',
    background: '#ecf0f1',
    padding: '6px 14px',
    borderRadius: 4,
    textDecoration: 'none',
    fontSize: '0.875rem',
    fontWeight: 500,
  },
  main: {
    flex: 1,
    maxWidth: 720,
    width: '100%',
    margin: '32px auto',
    padding: '0 16px',
    display: 'flex',
    flexDirection: 'column',
    gap: 24,
  },
  card: {
    background: '#282828',
    borderRadius: 12,
    padding: 24,
    display: 'flex',
    flexDirection: 'column',
    gap: 12,
  },
  sectionTitle: {
    fontSize: '1rem',
    fontWeight: 600,
    color: '#bbb',
    textTransform: 'uppercase',
    letterSpacing: '0.06em',
  },
  hint: {
    fontSize: '0.875rem',
    color: '#888',
    lineHeight: 1.5,
  },
  badge: {
    display: 'inline-block',
    padding: '12px 28px',
    borderRadius: 30,
    fontSize: '1.1rem',
    fontWeight: 700,
    color: '#fff',
    alignSelf: 'flex-start',
    transition: 'background 0.3s',
  },
  uploadBtn: {
    padding: '14px 20px',
    border: 'none',
    borderRadius: 8,
    color: '#fff',
    fontSize: '0.95rem',
    fontWeight: 600,
    cursor: 'pointer',
    transition: 'background 0.2s, opacity 0.2s',
    width: '100%',
  },
  emptyLog: {
    color: '#555',
    fontSize: '0.875rem',
    fontStyle: 'italic',
  },
  logList: {
    listStyle: 'none',
    display: 'flex',
    flexDirection: 'column',
    gap: 8,
    maxHeight: 420,
    overflowY: 'auto',
  },
  logItem: {
    background: '#333',
    borderLeft: '4px solid #555',
    borderRadius: '0 6px 6px 0',
    padding: '10px 14px',
    display: 'flex',
    gap: 12,
    alignItems: 'flex-start',
    fontSize: '0.875rem',
    lineHeight: 1.5,
  },
  logTime: {
    color: '#666',
    fontSize: '0.75rem',
    whiteSpace: 'nowrap',
    paddingTop: 2,
    minWidth: 64,
  },
};

// New styles for voice indicator
styles.voiceIndicator = {
  position: 'absolute',
  top: 16,
  right: 16,
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  zIndex: 40,
};

styles.statusDot = {
  width: 14,
  height: 14,
  borderRadius: '50%',
  boxShadow: '0 0 8px rgba(0,0,0,0.6)',
  border: '2px solid rgba(255,255,255,0.08)'
};

styles.statusText = {
  fontSize: '0.9rem',
  fontWeight: 600,
  color: '#e0e0e0'
};