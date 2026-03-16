import React, { useState, useEffect, useRef, useCallback } from "react";
import WhisperWorker from "./whisperWorker?worker";
import PdfViewer from "./PdfViewer.jsx";

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
  const [wakeWordReady, setWakeWordReady] = useState(false);
  const [speechMode, setSpeechMode] = useState("auto"); // auto | webspeech-only | whisper-only
  const [pendingAction, _setPendingActionState] = useState(null); // { type: "SHOW_RECENT_FILES", files } | { type: "SELECT_DOCUMENT", id, name } | null
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
  const recognitionRef      = useRef(null);
  const recognitionRunningRef = useRef(false);
  const recognitionRestartTimeoutRef = useRef(null);
  const recognitionBlockedRef = useRef(false);
  const ensureRecognitionRunningRef = useRef(() => {});
  const vadRef              = useRef(null);   // VAD removed; kept so optional calls are no-ops
  const listeningTimeoutRef = useRef(null);
  const workerRef           = useRef(null);
  const sendToAIRef         = useRef(null);
  const whisperFallbackNeededRef = useRef(false);
  const webSpeechHandledRef      = useRef(false);
  const lowConfidenceTranscriptRef = useRef(null);
  const webSpeechSubmitBlockedRef = useRef(false); // block Web Speech submits while Whisper fallback is active
  // Tracks the latest pendingAction value without closure-staleness issues.
  // Updated synchronously inside the setPendingAction wrapper defined above.
  const pendingActionRef = useRef(null);

  const WEB_SPEECH_CONFIDENCE_THRESHOLD = 0.72;
  const isWebSpeechOnly = speechMode === "webspeech-only";
  const isWhisperOnly = speechMode === "whisper-only";
  const API_TIMEOUT_MS = 20_000;
  const PREVIEW_TIMEOUT_MS = 6_000;
  const DOCUMENTS_TIMEOUT_MS = 10_000;
  const RESTART_BACKOFF_BASE_MS = 1000;
  const RESTART_BACKOFF_MAX_MS = 30_000;
  // How long to keep the mic listening after showing the file list so the user
  // can say a number without repeating the "activate" wake-word.
  const FILE_SELECTION_TIMEOUT_MS = 60_000;

  const normalizeCommandText = useCallback((value) => {
    const raw = (value ?? "").trim();
    if (!raw) return "";
    const withoutWakeWord = raw.replace(/^activate[\s,:-]*/i, "").trim();
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

  // VAD/Whisper removed: keep a harmless placeholder used by the UI so
  // existing button/label logic doesn't need widespread changes.
  const vad = { loading: false, errored: null, userSpeaking: false };

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
    webSpeechSubmitBlockedRef.current = false;
    lowConfidenceTranscriptRef.current = null;
    setStatus("idle");
    try { vadRef.current?.pause(); } catch (_) {}
    // Keep SpeechRecognition running continuously; do not restart here.
    // (Previously we restarted the mic after a short delay to avoid
    // iOS Safari ONNX/WASM memory issues when recreating audio sessions.)
    // Leaving recognition running prevents the mic from being turned off/on.
  }, [RESUME_DELAY_MS]);

  const resumeListeningRef = useRef(resumeListening);
  useEffect(() => { resumeListeningRef.current = resumeListening; }, [resumeListening]);

  // Keep a ref copy of `status` for the recognition watchdog to avoid
  // stale closure values inside the `onend` handler.
  const statusRef = useRef(status);
  useEffect(() => { statusRef.current = status; }, [status]);

  const restartAttemptsRef = useRef(0);

  const openDocumentPreview = useCallback((document) => {
    if (!document) return;

    const previewId = document.previewId ?? document.managedFileId ?? document.id;
    const name = document.name ?? document.originalFileName ?? document.fileName ?? "Document";

    if (!previewId) {
      addLog("❌ Could not open the selected document.", "error");
      setPendingAction(null);
      setAiResponse("Could not open that document.");
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
        setPendingAction(null);
        setAiResponse("No uploaded files were found. Please upload a file first.");
        return;
      }

      setAiResponse(null);
      setPendingAction({ type: "SHOW_RECENT_FILES", files: recentFiles });
    } catch (err) {
      addLog(`❌ Recent files error: ${err.message}`, "error");
      setPendingAction(null);
      setAiResponse("I couldn't load the recent files list.");
    }
  }, [addLog, fetchJsonWithTimeout]);

  // ── Send transcript to AI ─────────────────────────────────────────────────
  const sendToAI = useCallback(async (text) => {
    // When keepMicActive is true the finally block keeps the mic in listening
    // state instead of resetting to idle. Used after showing the file list so
    // the user can say a number without repeating "activate".
    let keepMicActive = false;
    try {
      const commandText = normalizeCommandText(text);
      if (!commandText) {
        addLog("⚠️ Command was empty after wake-word cleanup.", "warn");
        setPendingAction(null);
        setAiResponse("I didn't catch that command. Please try again.");
        return;
      }

      // ── Shortcut: numeric file selection when the recent-files list is shown ─
      const capturedPending = pendingActionRef.current;
      if (capturedPending?.type === "SHOW_RECENT_FILES") {
        const num = parseFileSelectionNumber(commandText);
        if (num !== null) {
          const files = capturedPending.files ?? [];
          if (num >= 1 && num <= files.length) {
            addLog(`📂 Opening file ${num}: "${files[num - 1].name}"`, "voice");
            setTranscript(commandText);
            setPendingAction(null);
            openDocumentPreview(files[num - 1]);
            return; // mic will reset via finally (keepMicActive = false)
          } else {
            // Number out of range — keep the list visible and stay listening.
            addLog(`⚠️ File number ${num} is not in the list (1–${files.length}).`, "warn");
            setTranscript(commandText);
            setAiResponse(`There are only ${files.length} files. Say a number between 1 and ${files.length}.`);
            setPendingAction(capturedPending); // restore list
            keepMicActive = true;
            return;
          }
        }
      }

      // Preview whether the backend will delegate this request to the advanced model
      // Clear previous state first to avoid stale badge persisting on preview error
      setUsedAdvancedModel(false);
      try {
        const previewJson = await fetchJsonWithTimeout('/api/process/preview', {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ input: commandText })
        }, PREVIEW_TIMEOUT_MS);
        const willUse = !!previewJson?.willUseAdvanced;
        setUsedAdvancedModel(willUse);
      } catch (previewErr) {
        addLog(`⚠️ Preview check failed: ${previewErr?.message || 'Unknown error'}`, "warn");
        setUsedAdvancedModel(false);
      }
      addLog(`🤖 Sending to AI: \"${commandText}\"`, "info");
      const data = await fetchJsonWithTimeout("/api/process", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ input: commandText }),
      }, API_TIMEOUT_MS);
      const answer = (data.response || "").trim();
      const usedAdvanced = data?.usedAdvancedModel ?? data?.UsedAdvancedModel ?? data?.usedAdvanced ?? false;

      if (!answer) {
        addLog("⚠️ AI returned an empty response.", "warn");
        setPendingAction(null);
        setAiResponse("I didn't get a response for that command. Please try again.");
        setUsedAdvancedModel(usedAdvanced);
        return;
      }

      // ── Action token interception ────────────────────────────────────────
      if (answer === "__ACTION:OPEN_FILE_PICKER__" || answer === "__ACTION:SHOW_RECENT_FILES__") {
        addLog("📂 Command: show recent uploaded files", "voice");
        setAiResponse(null);
        await fetchRecentDocuments();
        // Keep mic active so the user can immediately say a number.
        keepMicActive = true;
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
          setAiResponse("No document is open. Open a document first.");
        }
      } else if (answer === "__ACTION:PREVIOUS_PDF_PAGE__") {
        addLog("📄 Command: previous page", "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current) {
          pdfViewerRef.current.prevPage();
        } else {
          setAiResponse("No document is open. Open a document first.");
        }
      } else if (answer.startsWith("__ACTION:HIGHLIGHT_WORD:")) {
        const word = answer.replace("__ACTION:HIGHLIGHT_WORD:", "").replace(/__$/, "").trim();
        addLog(`🖊️ Command: highlight "${word}"`, "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current && word) {
          pdfViewerRef.current.highlightWord(word);
        } else if (!pdfViewerRef.current) {
          setAiResponse("No document is open. Open a document first.");
        }
      } else if (answer === "__ACTION:REMOVE_HIGHLIGHTS__") {
        addLog("🗑️ Command: remove all highlights", "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current) {
          pdfViewerRef.current.removeHighlights();
        } else {
          setAiResponse("No document is open.");
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
          setAiResponse("No document is open. Open a document first.");
        } else {
          setAiResponse("Sorry, I couldn't understand the page number.");
        }
      } else if (answer.startsWith("__ACTION:GO_TO_LOINC:")) {
        const loincCode = answer.replace("__ACTION:GO_TO_LOINC:", "").replace(/__$/, "").trim();
        addLog(`🔬 Command: go to LOINC code "${loincCode}"`, "voice");
        setAiResponse(null);
        setPendingAction(null);
        if (pdfViewerRef.current && loincCode) {
          pdfViewerRef.current.goToLoincCode(loincCode);
        } else if (!pdfViewerRef.current) {
          setAiResponse("No document is open. Open a document first.");
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
      } else {
        addLog(`💡 AI: "${answer}"`, "voice");
        setPendingAction(null);
        setAiResponse(answer);
        setUsedAdvancedModel(usedAdvanced);
      }
    } catch (err) {
      const errorMessage = err?.message || "Unknown error";
      addLog(`❌ AI error: ${errorMessage}`, "error");
      setUsedAdvancedModel(false);
      setPendingAction(null);
      setAiResponse(/timed out/i.test(errorMessage)
        ? "The request timed out. Please try again."
        : "I couldn't process that command right now. Please try again.");
    } finally {
      if (keepMicActive) {
        // Stay in listening state so the user can say a file number directly.
        // Reset speech-recognition state so the next utterance is processed.
        activatedRef.current = true;
        webSpeechHandledRef.current = false;
        whisperFallbackNeededRef.current = false;
        webSpeechSubmitBlockedRef.current = false;
        lowConfidenceTranscriptRef.current = null;
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
  }, [addLog, fetchRecentDocuments, fetchJsonWithTimeout, normalizeCommandText, openDocumentPreview, setPendingAction]);

  sendToAIRef.current = sendToAI;
  useEffect(() => { sendToAIRef.current = sendToAI; }, [sendToAI]);

  // ── SpeechRecognition — "activate" keyword only ───────────────────────────
  useEffect(() => {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) {
      setWakeWordReady(false);
      addLog("⚠️ Wake-word not available — use the mic button below.", "warn");
      return;
    }

    let disposed = false;
    const rec = new SR();
    rec.continuous     = true;
    rec.interimResults = true;
    rec.lang           = "en-US";

    const clearRecognitionRestart = () => {
      if (recognitionRestartTimeoutRef.current) {
        window.clearTimeout(recognitionRestartTimeoutRef.current);
        recognitionRestartTimeoutRef.current = null;
      }
    };

    const markRecognitionUnavailable = (message) => {
      recognitionBlockedRef.current = true;
      recognitionRunningRef.current = false;
      clearRecognitionRestart();
      setWakeWordReady(false);
      addLog(message, "error");
    };

    const scheduleRecognitionRestart = (reason = "") => {
      if (disposed || recognitionBlockedRef.current) return;

      clearRecognitionRestart();
      setWakeWordReady(false);

      const attempt = restartAttemptsRef.current;
      const delay = Math.min(RESTART_BACKOFF_BASE_MS * (2 ** attempt), RESTART_BACKOFF_MAX_MS);
      restartAttemptsRef.current = attempt + 1;

      if (attempt === 0 || reason) {
        addLog(`⚠️ Wake-word listener stopped${reason ? ` (${reason})` : ""} — retrying.`, "warn");
      }

      recognitionRestartTimeoutRef.current = window.setTimeout(() => {
        if (disposed) return;
        ensureRecognitionRunningRef.current();
      }, delay);
    };

    const ensureRecognitionRunning = () => {
      if (disposed || recognitionBlockedRef.current || recognitionRunningRef.current) return;

      clearRecognitionRestart();

      try {
        rec.start();
      } catch (err) {
        const message = `${err?.message || err || ""}`;
        if (/already started/i.test(message)) {
          recognitionRunningRef.current = true;
          setWakeWordReady(true);
          return;
        }
        if (/not-allowed|permission|denied|audio-capture/i.test(message)) {
          markRecognitionUnavailable(`🔴 Mic error: ${message || "microphone unavailable"}`);
          return;
        }
        scheduleRecognitionRestart("start failed");
      }
    };

    ensureRecognitionRunningRef.current = ensureRecognitionRunning;

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

        // VAD disabled: we do not start the VAD here.
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
        webSpeechSubmitBlockedRef.current = true;
        return;
      }

      if (confidence != null && confidence < WEB_SPEECH_CONFIDENCE_THRESHOLD && !isWebSpeechOnly) {
        whisperFallbackNeededRef.current = true;
        lowConfidenceTranscriptRef.current = latestText;
        setStatus("processing");
        addLog(`⚠️ Web Speech confidence ${confidence.toFixed(2)} is low — using Whisper fallback.`, "warn");
        webSpeechSubmitBlockedRef.current = true;
        return;
      }

      whisperFallbackNeededRef.current = false;
      lowConfidenceTranscriptRef.current = null;
      if (webSpeechSubmitBlockedRef.current) {
        addLog("⚠️ Web Speech submit blocked while Whisper fallback active.", "info");
        return;
      }
      webSpeechHandledRef.current = true;
      addLog(confidence != null
        ? `🗣️ Web Speech (${confidence.toFixed(2)}): "${latestText}"`
        : `🗣️ Web Speech: "${latestText}"`, "voice");
      setTranscript(latestText);
      setAiResponse(null);
      setStatus("sending");
      sendToAIRef.current?.(latestText);
    };

    rec.onerror = (e) => {
      if (e.error === "not-allowed" || e.error === "audio-capture") {
        markRecognitionUnavailable(`🔴 Mic error: ${e.error} — microphone cannot be accessed.`);
        return;
      }
      if (e.error !== "no-speech" && e.error !== "aborted")
        addLog(`Mic error: ${e.error}`, "warn");
    };

    rec.onstart = () => {
      recognitionBlockedRef.current = false;
      recognitionRunningRef.current = true;
      setWakeWordReady(true);
      // Successful start — reset restart attempts.
      restartAttemptsRef.current = 0;
    };

    rec.onend = () => {
      recognitionRunningRef.current = false;
      setWakeWordReady(false);
      if (!disposed) {
        scheduleRecognitionRestart("ended");
      }
    };

    recognitionRef.current = rec;
    ensureRecognitionRunning();

    return () => {
      disposed = true;
      ensureRecognitionRunningRef.current = () => {};
      clearRecognitionRestart();
      recognitionRef.current = null;
      recognitionRunningRef.current = false;
      setWakeWordReady(false);
      try { rec.abort(); } catch (_) {}
    };
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
          setStatus("sending");
          sendToAIRef.current?.(cleaned);
          webSpeechSubmitBlockedRef.current = false;
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
            setStatus("sending");
            sendToAIRef.current?.(fallbackText);
            webSpeechSubmitBlockedRef.current = false;
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
          setStatus("sending");
          sendToAIRef.current?.(fallbackText);
          webSpeechSubmitBlockedRef.current = false;
          return;
        }
        addLog(`❌ Whisper: ${message}`, "error");
        resumeListeningRef.current();
      }
    };

    worker.onerror = (err) => {
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
        setStatus("sending");
        sendToAIRef.current?.(fallbackText);
        webSpeechSubmitBlockedRef.current = false;
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
      ensureRecognitionRunningRef.current();
      setStatus("listening");
      setTranscript(null);
      setAiResponse(null);
      addLog("🎤 Listening… speak your command", "info");
      if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
      listeningTimeoutRef.current = setTimeout(() => {
        if (activatedRef.current) { addLog("⏱️ No speech detected — resetting.", "warn"); resumeListening(); }
      }, 30_000);
      // VAD disabled: do not start VAD on manual mic button
    } else if (status === "listening") {
      if (listeningTimeoutRef.current) { clearTimeout(listeningTimeoutRef.current); listeningTimeoutRef.current = null; }
      activatedRef.current = false;
      try { vadRef.current?.pause(); } catch (_) {}
      setStatus("idle");
      addLog("Cancelled.", "info");
        // Do not restart the recognition here — keep the mic running continuously.
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
        : !wakeWordReady
          ? "🎤 Reconnecting wake-word…"
        : vad.loading
          ? "⏱️ Loading Silero VAD…"
          : vad.errored
            ? "⚠️ VAD error — say Activate to retry"
            : 'Say "Activate"';
  const dotColor = isSending    ? "#3498db"
    : isProcessing ? "#9b59b6"
    : isListening  ? "#e67e22"
    : !wakeWordReady ? "#f1c40f"
    : vad.errored  ? "#e74c3c"
    : "#00b894";

  const shouldPulse = vad.loading || isProcessing || isSending;
  const dotStyle = { ...styles.dot, background: dotColor, ...(shouldPulse ? { animation: "pulse 1s ease-in-out infinite" } : {}) };

  return (
    <div style={styles.page}>
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

        {(transcript || isSending || aiResponse || pendingAction) && (
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
            {pendingAction?.type === "SHOW_RECENT_FILES" && (
              <div style={styles.actionCard}>
                <p style={styles.actionCardTitle}>📂 Recent Uploaded Files</p>
                <p style={styles.actionCardHint}>Say a number (1–{pendingAction.files.length}) or tap a file to open it.</p>
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
                <p style={styles.actionCardTitle}>📄 Last Uploaded Document</p>
                <p style={styles.actionCardDoc}>{pendingAction.name}</p>
                <button
                  style={styles.actionBtnSecondary}
                  onClick={() => openDocumentPreview({ previewId: pendingAction.id, name: pendingAction.name })}
                >
                  Preview Document →
                </button>
              </div>
            )}
          </section>
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

function formatActionTime(value) {
  if (!value) return "";

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "";

  return parsed.toLocaleString();
}

// Parses a voice/typed command into a 1-based file-list index.
// Handles bare digits ("2"), "file 2", "number 3", "second", "open the third", etc.
// Returns a number 1–9 when a numeric intent is detected; the caller is
// responsible for bounds-checking against the actual file-list length.
function parseFileSelectionNumber(text) {
  if (!text) return null;
  const s = text.trim().toLowerCase();

  // Word ordinals / cardinals (covers the typical 1–5 list size)
  const words = { one: 1, first: 1, two: 2, second: 2, three: 3, third: 3, four: 4, fourth: 4, five: 5, fifth: 5 };
  for (const [word, num] of Object.entries(words)) {
    if (new RegExp(`\\b${word}\\b`).test(s)) return num;
  }

  // Patterns like "file 2", "number 3", "open 4", "#5", "pick 1", "select 2"
  const prefixMatch = s.match(/(?:file|number|open|pick|select|item)\s*#?\s*([1-9])\b/);
  if (prefixMatch) return parseInt(prefixMatch[1], 10);

  // Bare single digit (possibly surrounded by whitespace or punctuation)
  const bareMatch = s.match(/^\s*([1-9])\s*$/);
  if (bareMatch) return parseInt(bareMatch[1], 10);

  return null;
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
