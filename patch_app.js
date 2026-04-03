const fs = require('fs');

let code = fs.readFileSync('src/SmartGlassesMiddleware/client/src/App.jsx', 'utf8');

// 1. Add pendingCommandsRef
code = code.replace(
  `  const [groqCommandTranscriptionEnabled, setGroqCommandTranscriptionEnabled] = useState(true);`,
  `  const [groqCommandTranscriptionEnabled, setGroqCommandTranscriptionEnabled] = useState(true);\n  const pendingCommandsRef = useRef(new Map());`
);

// 2. Add preSpeechBuffer logic and modify the event loop
// We need to find the definition of the variables inside `useEffect`
code = code.replace(
  `    let speechChunks  = [];   // Float32Array chunks of the current speech burst`,
  `    let speechChunks  = [];   // Float32Array chunks of the current speech burst\n    let preSpeechBuffer = []; // Rolling buffer of the last ~500ms of audio before speech\n    const MAX_PRE_SPEECH_CHUNKS = Math.ceil((SAMPLE_RATE * 0.5) / BUFFER_SIZE);`
);

// Update submitCommand to parallelize Groq and Local Whisper
const oldSubmitCommand = `    async function submitCommand() {
      if (disposed || !activatedRef.current) return;
      clearTimers();
      const audio   = concatF32(commandChunks);
      commandChunks = [];
      const partial = partialTexts.join(" ").trim();
      partialTexts  = [];

      if (audio.length < SAMPLE_RATE * MIN_COMMAND_SEC) {
        if (partial) {
          // Wake-word + inline command detected; audio window was too short but the
          // text from the wake-word utterance is reliable — send it directly to AI.
          activatedRef.current = false;
          showTranscript(partial);
          setAiResponse(null);
          setStatus("sending");
          sendToAIRef.current?.(partial);
        } else {
          // Too short — almost certainly noise or misfire
          resumeListeningRef.current();
        }
        return;
      }

      activatedRef.current = false;
      if (partial) showTranscript(partial); // preview while final runs
      setStatus("processing");

      // Primary path: Groq Whisper Turbo for the final command transcript.
      // Fallback path: local Whisper worker command mode.
      const groqText = await transcribeCommandWithGroqRef.current?.(audio);
      if (groqText && groqText.length >= 2 && /[A-Za-z0-9]/.test(groqText)) {
        addLog(\`🗣️ Groq Whisper Turbo: "\${groqText}"\`, "voice");
        showTranscript(groqText);
        setAiResponse(null);
        setStatus("sending");
        sendToAIRef.current?.(groqText);
        return;
      }

      addLog("⚠️ Falling back to local Whisper command transcription", "warn");
      workerRef.current?.postMessage({ type: "transcribe", audio, mode: "command" });
    }`;

const newSubmitCommand = `    async function submitCommand() {
      if (disposed || !activatedRef.current) return;
      clearTimers();
      const audio   = concatF32(commandChunks);
      commandChunks = [];
      const partial = partialTexts.join(" ").trim();
      partialTexts  = [];

      if (audio.length < SAMPLE_RATE * MIN_COMMAND_SEC) {
        if (partial) {
          // Wake-word + inline command detected; audio window was too short but the
          // text from the wake-word utterance is reliable — send it directly to AI.
          activatedRef.current = false;
          showTranscript(partial);
          setAiResponse(null);
          setStatus("sending");
          sendToAIRef.current?.(partial);
        } else {
          // Too short — almost certainly noise or misfire
          resumeListeningRef.current();
        }
        return;
      }

      activatedRef.current = false;
      if (partial) showTranscript(partial); // preview while final runs
      setStatus("processing");

      // Dispatch both Groq and Local Whisper concurrently
      const commandId = Date.now().toString() + Math.random().toString();

      const localWhisperPromise = new Promise((resolve) => {
        pendingCommandsRef.current.set(commandId, resolve);
        workerRef.current?.postMessage({ type: "transcribe", audio, mode: "command", commandId });
      });

      const groqPromise = (async () => {
        const text = await transcribeCommandWithGroqRef.current?.(audio);
        if (text && text.length >= 2 && /[A-Za-z0-9]/.test(text)) {
           return { source: 'groq', text };
        }
        throw new Error("Groq failed or invalid result");
      })();

      const isValidResult = (text) => text && text.length >= 2 && /[A-Za-z0-9]/.test(text);

      try {
        // Try to get the first successful valid result
        let result = await Promise.any([
          groqPromise,
          localWhisperPromise.then(text => {
            if (isValidResult(text)) return { source: 'local', text };
            throw new Error("Local Whisper failed or invalid result");
          })
        ]);

        if (result.source === 'groq') {
          addLog(\`🗣️ Groq Whisper Turbo: "\${result.text}"\`, "voice");
        } else {
          addLog(\`🗣️ Local Whisper: "\${result.text}"\`, "voice");
        }

        showTranscript(result.text);
        setAiResponse(null);
        setStatus("sending");
        sendToAIRef.current?.(result.text);

      } catch (err) {
        // Both failed
        addLog("(No speech detected in command)", "warn");
        resumeListeningRef.current();
      } finally {
        pendingCommandsRef.current.delete(commandId);
      }
    }`;

code = code.replace(oldSubmitCommand, newSubmitCommand);

// Update worker.onmessage to resolve pending commands and handle preSpeechBuffer
const oldWorkerMessage = `      if (mode === "command") {
        const meaningful = cleaned.length >= 2 && /[A-Za-z0-9]/.test(cleaned);
        if (meaningful) {
          addLog(\`🗣️ Whisper: "\${cleaned}"\`, "voice");
          showTranscript(cleaned);
          setAiResponse(null);
          setStatus("sending");
          sendToAIRef.current?.(cleaned);
        } else {
          addLog("(No speech detected in command)", "warn");
          resumeListeningRef.current();
        }
      }`;

const newWorkerMessage = `      if (mode === "command") {
        const commandId = e.data.commandId;
        if (commandId && pendingCommandsRef.current.has(commandId)) {
          const resolveFn = pendingCommandsRef.current.get(commandId);
          resolveFn(cleaned);
          pendingCommandsRef.current.delete(commandId);
        } else {
          // If for some reason we don't have a promise waiting, handle it directly
          const meaningful = cleaned.length >= 2 && /[A-Za-z0-9]/.test(cleaned);
          if (meaningful) {
            addLog(\`🗣️ Whisper: "\${cleaned}"\`, "voice");
            showTranscript(cleaned);
            setAiResponse(null);
            setStatus("sending");
            sendToAIRef.current?.(cleaned);
          } else {
            addLog("(No speech detected in command)", "warn");
            resumeListeningRef.current();
          }
        }
      }`;

code = code.replace(oldWorkerMessage, newWorkerMessage);

// Update error handling for worker
const oldWorkerError = `      if (type === "error") {
        addLog(\`❌ Whisper: \${message}\`, "error");
        if (mode === "command") resumeListeningRef.current();
        return;
      }`;

const newWorkerError = `      if (type === "error") {
        addLog(\`❌ Whisper: \${message}\`, "error");
        if (mode === "command") {
          const commandId = e.data.commandId;
          if (commandId && pendingCommandsRef.current.has(commandId)) {
            const resolveFn = pendingCommandsRef.current.get(commandId);
            resolveFn(""); // Resolve with empty to fail the isValidResult check and trigger throw
            pendingCommandsRef.current.delete(commandId);
          } else {
            resumeListeningRef.current();
          }
        }
        return;
      }`;

code = code.replace(oldWorkerError, newWorkerError);

// Pre-speech buffer logic insertion
const oldProcessorLogic = `          if (rms >= RMS_THRESHOLD) {
            isSpeaking  = true;
            silenceSince = 0;
            speechChunks.push(samples);
            if (activatedRef.current) {
              commandChunks.push(samples);
            } else {
              if (now - lastWakeCheckTime >= 500 && speechChunks.length > 0) {
                lastWakeCheckTime = now;
                const audio = concatF32(speechChunks);
                if (audio.length >= SAMPLE_RATE * WAKE_MIN_CHUNK_SEC) {
                  workerRef.current?.postMessage({ type: "transcribe", audio, mode: "wake-word" });
                }
              }
            }
          } else if (isSpeaking) {`;

const newProcessorLogic = `          if (rms >= RMS_THRESHOLD) {
            if (!isSpeaking) {
              // Just started speaking, flush the pre-speech buffer into our active chunks
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
              commandChunks.push(samples);
            } else {
              if (now - lastWakeCheckTime >= 500 && speechChunks.length > 0) {
                lastWakeCheckTime = now;
                const audio = concatF32(speechChunks);
                if (audio.length >= SAMPLE_RATE * WAKE_MIN_CHUNK_SEC) {
                  workerRef.current?.postMessage({ type: "transcribe", audio, mode: "wake-word" });
                }
              }
            }
          } else if (isSpeaking) {`;

code = code.replace(oldProcessorLogic, newProcessorLogic);

const oldElseNotSpeaking = `          } else if (isSpeaking) {
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
                  const mode = activatedRef.current ? "partial" : "wake-word";
                  workerRef.current?.postMessage({ type: "transcribe", audio, mode });
                }
              }
            }
          }
        };`;

const newElseNotSpeaking = `          } else if (isSpeaking) {
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
                  const mode = activatedRef.current ? "partial" : "wake-word";
                  workerRef.current?.postMessage({ type: "transcribe", audio, mode });
                }
              }
            }
          } else {
            // Not speaking, maintain a rolling buffer of audio right before speech starts
            preSpeechBuffer.push(samples);
            if (preSpeechBuffer.length > MAX_PRE_SPEECH_CHUNKS) {
              preSpeechBuffer.shift();
            }
          }
        };`;

code = code.replace(oldElseNotSpeaking, newElseNotSpeaking);

fs.writeFileSync('src/SmartGlassesMiddleware/client/src/App.jsx', code);
