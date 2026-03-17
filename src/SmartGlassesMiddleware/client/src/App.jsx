import React, { useState, useEffect, useRef, useCallback } from "react";
import WhisperWorker from "./whisperWorker?worker";
import PdfViewer from "./PdfViewer.jsx";

// ─── MAIN COMPONENT ───────────────────────────────────────────────────────────
//
// Voice pipeline: Whisper WebGPU (single multilingual whisper-base model)
//   - AudioContext + ScriptProcessorNode captures mic at 16 kHz
//   - Amplitude VAD detects speech vs silence
//   - Wake-word mode: each speech chunk sent to Whisper; "activate" triggers command mode
//   - Command mode: chunks sent as partial results; sentence-end pattern or 2.5 s
//     silence submits the full accumulated audio for final transcription
//
export default function App() {
  const [status, setStatus]       = useState("idle"); // idle | listening | processing | sending
  const [logs, setLogs]           = useState([]);
  const [transcript, setTranscript] = useState(null);
  const [aiResponse, setAiResponse] = useState(null);
  const [usedAdvancedModel, setUsedAdvancedModel] = useState(false);
  const [wakeWordReady, setWakeWordReady] = useState(false);
  const [pendingAction, _setPendingActionState] = useState(null); // { type: "SHOW_RECENT_FILES", files } | { type: "SELECT_DOCUMENT", id, name } | null
  // Reminders toast: null | { reminders: [], phase: 'visible'|'fading' }
  const [remindersToast, setRemindersToast] = useState(null);
  // Show landing screen until the user says "activate" for the first time.
  const [showLanding, setShowLanding] = useState(() => {
    try { return !localStorage.getItem("sgm_activated"); } catch (_) { return true; }
  });
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
  const workerRef           = useRef(null);
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
  const PREVIEW_TIMEOUT_MS = 6_000;
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
      aiResponseDismissRef.current = setTimeout(() => setAiResponse(null), BUBBLE_DISMISS_MS);
    }
  }, []);

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
    return () => {
      if (transcriptDismissRef.current) clearTimeout(transcriptDismissRef.current);
      if (aiResponseDismissRef.current) clearTimeout(aiResponseDismissRef.current);
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
        showAiResponse("I didn't catch that command. Please try again.");
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
            showTranscript(commandText);
            setPendingAction(null);
            openDocumentPreview(files[num - 1]);
            return; // mic will reset via finally (keepMicActive = false)
          } else {
            // Number out of range — keep the list visible and stay listening.
            addLog(`⚠️ File number ${num} is not in the list (1–${files.length}).`, "warn");
            showTranscript(commandText);
            showAiResponse(`There are only ${files.length} files. Say a number between 1 and ${files.length}.`);
            setPendingAction(capturedPending); // restore list
            keepMicActive = true;
            return;
          }
        }
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

        const goToPageMatch = lowered.match(/\b(?:go to|goto|open)?\s*page\s+(\d{1,4})\b/);
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

        if (/\b(?:remove|clear)\s+(?:all\s+)?highlights?\b/.test(lowered)) {
          addLog("🗑️ Command: remove all highlights", "voice");
          setAiResponse(null);
          setPendingAction(null);
          localPdf.removeHighlights();
          return;
        }

        if (/\bclose\s+(?:the\s+)?(?:document|pdf)\b/.test(lowered)) {
          addLog("📄 Command: close document", "voice");
          setAiResponse(null);
          setPendingAction(null);
          setOpenPdf(null);
          return;
        }

        const highlightMatch = commandText.match(/\bhighlight\s+(.+)$/i);
        if (highlightMatch) {
          const phrase = highlightMatch[1]
            .replace(/^the\s+/i, "")
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
        showAiResponse("I didn't get a response for that command. Please try again.");
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
  }, [addLog, fetchRecentDocuments, fetchJsonWithTimeout, normalizeCommandText, openDocumentPreview, openPdf, setPendingAction, showAiResponse, showRemindersToast, showTranscript]);

  sendToAIRef.current = sendToAI;
  useEffect(() => { sendToAIRef.current = sendToAI; }, [sendToAI]);

  // ── Voice pipeline: Whisper WebGPU + AudioContext mic capture ────────────
  //
  // One multilingual whisper-base model handles all modes:
  //   'wake-word' — short bursts checked for the keyword "activate"
  //   'partial'   — each speech burst during a command; result streams to UI
  //   'command'   — full accumulated audio; final high-quality transcription
  //
  // Sentence-end detection: Whisper naturally adds `.` `?` `!` when it hears
  // a complete thought.  We detect these patterns and submit the command after
  // a short pause (SENTENCE_END_SILENCE_MS) rather than waiting for the full
  // force-submit timeout — this gives a much more natural end-of-utterance feel.
  useEffect(() => {
    const SAMPLE_RATE          = 16_000;
    const BUFFER_SIZE          = 2048;       // 128 ms per ScriptProcessor callback
    const RMS_THRESHOLD        = 0.012;      // RMS below this → silence
    const SILENCE_CHUNK_MS     = 550;        // consecutive silence before processing a speech burst
    const SENTENCE_END_MS      = 350;        // pause after sentence-end pattern → submit
    const FORCE_SUBMIT_MS      = 2_500;      // max silence during command → force submit
    const MIN_COMMAND_SEC      = 0.3;        // minimum audio duration (s) to bother submitting
    const MIN_CHUNK_SEC        = 0.15;       // minimum audio chunk (s) to send to Whisper

    let disposed = false;

    // Per-effect mutable state (local vars, no re-render on change)
    let speechChunks  = [];   // Float32Array chunks of the current speech burst
    let commandChunks = [];   // all audio accumulated since activation
    let partialTexts  = [];   // Whisper partial transcriptions so far
    let isSpeaking    = false;
    let silenceSince  = 0;

    let forceSubmitTimer    = null;
    let sentenceEndTimer    = null;

    function concatF32(chunks) {
      const total = chunks.reduce((s, c) => s + c.length, 0);
      const out = new Float32Array(total);
      let off = 0;
      for (const c of chunks) { out.set(c, off); off += c.length; }
      return out;
    }

    // Detect whether Whisper thinks the utterance is a complete sentence.
    // Whisper adds punctuation naturally; we also match common short commands
    // that may not get punctuation but are clearly self-contained.
    function isSentenceComplete(text) {
      if (!text) return false;
      const t = text.trim();
      if (/[.?!]\s*$/.test(t)) return true;
      if (/^(next|previous|prev)\s+(page|slide)$/i.test(t)) return true;
      if (/^(close|open)\s+(document|file|pdf|admin)$/i.test(t)) return true;
      if (/^(clear|remove)\s+(all\s+)?highlights?$/i.test(t)) return true;
      if (/^print(\s+(document|pdf))?$/i.test(t)) return true;
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

    function submitCommand() {
      if (disposed || !activatedRef.current) return;
      clearTimers();
      const audio   = concatF32(commandChunks);
      commandChunks = [];
      const partial = partialTexts.join(" ").trim();
      partialTexts  = [];

      if (audio.length < SAMPLE_RATE * MIN_COMMAND_SEC) {
        // Too short — almost certainly noise or misfire
        resumeListeningRef.current();
        return;
      }

      activatedRef.current = false;
      if (partial) showTranscript(partial); // preview while final runs
      setStatus("processing");
      workerRef.current?.postMessage({ type: "transcribe", audio, mode: "command" });
    }

    // Worker message handler (set synchronously so it is ready before startMic)
    const worker = new WhisperWorker();
    workerRef.current = worker;

    worker.onmessage = (e) => {
      if (disposed) return;
      const { type, text, message, mode } = e.data;

      if (type === "loading") {
        addLog(`⏳ ${message}`, "info");
        return;
      }

      if (type === "error") {
        addLog(`❌ Whisper: ${message}`, "error");
        if (mode === "command") resumeListeningRef.current();
        return;
      }

      if (type !== "result") return;

      const cleaned = (text || "").trim();

      if (mode === "wake-word") {
        // Only act on "activate" when not already active and not mid-request
        if (
          /\bactivate\b/i.test(cleaned) &&
          !activatedRef.current &&
          statusRef.current !== "sending" &&
          statusRef.current !== "processing"
        ) {
          const rest = cleaned.replace(/\bactivate\b[\s,:-]*/i, "").trim();
          activatedRef.current = true;
          commandChunks = [];
          partialTexts  = [];
          clearTimers();
          setStatus("listening");
          setShowLanding(false);
          try { localStorage.setItem("sgm_activated", "1"); } catch (_) {}
          addLog("🎤 Activated — speak your command", "info");

          if (rest.length >= 2) {
            // Wake word + command in the same utterance
            partialTexts.push(rest);
            setTranscript(rest);
            if (isSentenceComplete(rest)) {
              sentenceEndTimer = setTimeout(submitCommand, SENTENCE_END_MS);
            } else {
              forceSubmitTimer = setTimeout(submitCommand, FORCE_SUBMIT_MS);
            }
          } else {
            // Start idle timeout (no command heard yet)
            if (listeningTimeoutRef.current) clearTimeout(listeningTimeoutRef.current);
            listeningTimeoutRef.current = setTimeout(() => {
              if (activatedRef.current && commandChunks.length === 0) {
                addLog("⏱️ No command heard — resetting.", "warn");
                resumeListeningRef.current();
              }
            }, LISTENING_IDLE_TIMEOUT_MS);
          }
        }
        return;
      }

      if (mode === "partial" && activatedRef.current) {
        if (cleaned.length >= 2) {
          partialTexts.push(cleaned);
          setTranscript(partialTexts.join(" ")); // stream to UI

          if (isSentenceComplete(cleaned)) {
            // Sentence complete — schedule a short-pause submit
            clearTimers();
            sentenceEndTimer = setTimeout(submitCommand, SENTENCE_END_MS);
          } else {
            // Reset force-submit (user is still mid-sentence)
            if (forceSubmitTimer) clearTimeout(forceSubmitTimer);
            forceSubmitTimer = setTimeout(submitCommand, FORCE_SUBMIT_MS);
          }
        }
        return;
      }

      if (mode === "command") {
        const meaningful = cleaned.length >= 2 && /[A-Za-z0-9]/.test(cleaned);
        if (meaningful) {
          addLog(`🗣️ Whisper: "${cleaned}"`, "voice");
          showTranscript(cleaned);
          setAiResponse(null);
          setStatus("sending");
          sendToAIRef.current?.(cleaned);
        } else {
          addLog("(No speech detected in command)", "warn");
          resumeListeningRef.current();
        }
      }
    };

    worker.onerror = (err) => {
      if (disposed) return;
      const msg = err.message || String(err.type) || "unknown";
      addLog(`❌ Whisper worker crashed: ${msg}`, "error");
      resumeListeningRef.current();
    };

    // Start the mic capture loop
    async function startMic() {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        if (disposed) { stream.getTracks().forEach(t => t.stop()); return; }

        streamRef.current = stream;
        const ctx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: SAMPLE_RATE });
        audioCtxRef.current = ctx;
        const source    = ctx.createMediaStreamSource(stream);
        // NOTE: ScriptProcessorNode is deprecated; migrating to AudioWorklet requires
        // a dedicated worker module file which adds Vite build complexity.
        // TODO: replace with AudioWorkletNode once the project has a processor module.
        const processor = ctx.createScriptProcessor(BUFFER_SIZE, 1, 1);
        processorRef.current = processor;
        source.connect(processor);
        processor.connect(ctx.destination);

        processor.onaudioprocess = (ev) => {
          if (disposed) return;
          // Float32Array copy — the underlying buffer is reused after this callback
          const samples = new Float32Array(ev.inputBuffer.getChannelData(0));
          const rms = Math.sqrt(samples.reduce((s, v) => s + v * v, 0) / samples.length);
          const now = Date.now();

          if (rms >= RMS_THRESHOLD) {
            isSpeaking  = true;
            silenceSince = 0;
            speechChunks.push(samples);
            if (activatedRef.current) commandChunks.push(samples);
          } else if (isSpeaking) {
            if (!silenceSince) silenceSince = now;
            if ((now - silenceSince) >= SILENCE_CHUNK_MS) {
              isSpeaking = false;
              if (speechChunks.length > 0) {
                const audio = concatF32(speechChunks);
                speechChunks = [];
                silenceSince = 0;
                if (audio.length >= SAMPLE_RATE * MIN_CHUNK_SEC) {
                  const mode = activatedRef.current ? "partial" : "wake-word";
                  workerRef.current?.postMessage({ type: "transcribe", audio, mode });
                }
              }
            }
          }
        };

        if (!disposed) {
          setWakeWordReady(true);
          addLog("🎤 Microphone ready — say \"Activate\"", "info");
          // Warm the model so the first activation is instant
          worker.postMessage({ type: "preload" });
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
      try { processorRef.current?.disconnect(); } catch (_) {}
      try { audioCtxRef.current?.close(); } catch (_) {}
      if (streamRef.current) {
        streamRef.current.getTracks().forEach(t => t.stop());
        streamRef.current = null;
      }
      audioCtxRef.current  = null;
      processorRef.current = null;
      worker.terminate();
      workerRef.current = null;
      setWakeWordReady(false);
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Keepalive ping ────────────────────────────────────────────────────────
  // Ping the API every 2.5 minutes so the server (configured to scale-to-zero
  // after 30 min of inactivity) does not shut down while the tab is open.
  useEffect(() => {
    const PING_INTERVAL_MS = 2.5 * 60 * 1_000;
    const id = setInterval(async () => {
      try { await fetch("/api/process/client-settings", { method: "GET" }); } catch (_) {}
    }, PING_INTERVAL_MS);
    return () => clearInterval(id);
  }, []);

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

  // ── Render ────────────────────────────────────────────────────────────────
  const isListening  = status === "listening";
  const isProcessing = status === "processing";
  const isSending    = status === "sending";
  const btnLabel = isSending
    ? "🤖 Sending to AI…"
    : isProcessing
      ? "⏳ Transcribing…"
      : isListening
        ? "🗣️ Listening…"
        : !wakeWordReady
          ? "⏳ Loading Whisper…"
          : 'Say "Activate"';
  const dotColor = isSending    ? "#3498db"
    : isProcessing ? "#9b59b6"
    : isListening  ? "#e67e22"
    : !wakeWordReady ? "#f97316"
    : "#00b894";

  const shouldPulse = isProcessing || isSending || !wakeWordReady;
  const dotStyle = { ...styles.dot, background: dotColor, ...(shouldPulse ? { animation: "pulse 1s ease-in-out infinite" } : {}) };

  return (
    <div style={styles.page}>
      {/* ── Landing screen — shown until first activation ─────────────────────── */}
      {showLanding && (
        <div style={styles.landing}>
          <div style={styles.landingCard}>
            <h1 style={styles.landingTitle}>Smart Glasses Assistant</h1>
            <p style={styles.landingSubtitle}>
              Say <strong style={{color:"#f97316"}}>"Activate"</strong> to start, then speak your command.
            </p>
            <div style={styles.landingSection}>
              <h2 style={styles.landingH2}>🎤 How It Works</h2>
              <ol style={styles.landingOl}>
                <li>Say <em>"Activate"</em> to wake the assistant.</li>
                <li>Speak your command clearly.</li>
                <li>The assistant will respond or perform the action.</li>
                <li>Say "Activate" again for the next command.</li>
              </ol>
            </div>
            <div style={styles.landingSection}>
              <h2 style={styles.landingH2}>📋 Common Commands</h2>
              <table style={styles.landingTable}>
                <tbody>
                  {[
                    ["open file", "Show list of recent uploaded files"],
                    ["open last document", "Open the most recently uploaded document"],
                    ["next page", "Go to the next page"],
                    ["previous page", "Go to the previous page"],
                    ["go to page 5", "Jump to a specific page"],
                    ["highlight aspirin", "Highlight a word in the open document"],
                    ["clear highlights", "Remove all highlights"],
                    ["search for blood pressure", "Search for text in documents"],
                    ["search for BP", "Search using FDA SPL abbreviations"],
                    ["remind me to…", "Set a reminder"],
                    ["show my reminders", "List all your reminders"],
                    ["what time is it", "Get the current date and time"],
                    ["print document", "Print the open document with highlights"],
                    ["close document", "Close the open PDF"],
                    ["admin panel", "Open the admin panel"],
                  ].map(([cmd, desc]) => (
                    <tr key={cmd}>
                      <td style={styles.landingCmd}>"{cmd}"</td>
                      <td style={styles.landingDesc}>{desc}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p style={styles.landingDismiss}>This screen will disappear after your first activation.</p>
          </div>
        </div>
      )}

      {/* ── Command legend (always visible after first activation) ────────────── */}
      {!showLanding && (
        <div style={styles.legend}>
          <span style={styles.legendItem}><span style={styles.legendKbd}>Activate</span> wake</span>
          <span style={styles.legendItem}><span style={styles.legendKbd}>next / prev page</span> navigate</span>
          <span style={styles.legendItem}><span style={styles.legendKbd}>highlight [word]</span> mark</span>
          <span style={styles.legendItem}><span style={styles.legendKbd}>clear highlights</span> reset</span>
          <span style={styles.legendItem}><span style={styles.legendKbd}>print</span> print</span>
          <span style={styles.legendItem}><span style={styles.legendKbd}>open file</span> browse</span>
        </div>
      )}

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

        {/* ── Reminders toast — fades out like a notification ───────────── */}
        {remindersToast && (
          <div style={{
            ...styles.remindersToast,
            opacity:    remindersToast.phase === "fading" ? 0 : 1,
            transition: remindersToast.phase === "fading"
              ? "opacity 0.9s ease-out"
              : "opacity 0.25s ease-in",
          }}>
            <p style={styles.remindersToastTitle}>📝 Your Reminders</p>
            {remindersToast.reminders.length === 0 ? (
              <p style={styles.remindersToastEmpty}>No reminders yet.</p>
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
  // Landing screen overlay
  landing: {
    position: "fixed",
    inset: 0,
    background: "rgba(0,0,0,0.92)",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    zIndex: 100,
    padding: 16,
    overflowY: "auto",
  },
  landingCard: {
    background: "#111",
    border: "1px solid #333",
    borderRadius: 16,
    padding: "32px 28px",
    maxWidth: 620,
    width: "100%",
    display: "flex",
    flexDirection: "column",
    gap: 20,
  },
  landingTitle: {
    margin: 0,
    fontSize: "1.7rem",
    fontWeight: 700,
    color: "#fff",
    textAlign: "center",
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
    gap: 6,
    color: "#ddd",
    fontSize: "0.95rem",
    lineHeight: 1.6,
  },
  landingTable: {
    width: "100%",
    borderCollapse: "collapse",
    fontSize: "0.875rem",
  },
  landingCmd: {
    padding: "5px 10px 5px 0",
    color: "#f97316",
    fontFamily: "monospace",
    whiteSpace: "nowrap",
    verticalAlign: "top",
    width: "40%",
  },
  landingDesc: {
    padding: "5px 0",
    color: "#bbb",
    verticalAlign: "top",
  },
  landingDismiss: {
    margin: 0,
    fontSize: "0.8rem",
    color: "#555",
    textAlign: "center",
    fontStyle: "italic",
  },
  // Small command legend shown in top-left after first activation
  legend: {
    position: "absolute",
    top: 12,
    left: 12,
    display: "flex",
    flexWrap: "wrap",
    gap: "6px 12px",
    zIndex: 30,
    maxWidth: 480,
  },
  legendItem: {
    fontSize: "0.7rem",
    color: "#666",
    display: "flex",
    alignItems: "center",
    gap: 4,
  },
  legendKbd: {
    background: "#1a1a1a",
    border: "1px solid #333",
    borderRadius: 4,
    padding: "1px 5px",
    fontSize: "0.68rem",
    color: "#f97316",
    fontFamily: "monospace",
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
