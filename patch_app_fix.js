const fs = require('fs');

let code = fs.readFileSync('src/SmartGlassesMiddleware/client/src/App.jsx', 'utf8');

const oldWorkerMessage = `        const commandId = e.data.commandId;
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
        }`;

const newWorkerMessage = `        const commandId = e.data.commandId;
        if (commandId) {
          if (pendingCommandsRef.current.has(commandId)) {
            const resolveFn = pendingCommandsRef.current.get(commandId);
            resolveFn(cleaned);
            pendingCommandsRef.current.delete(commandId);
          } else {
            // Promise was already settled (likely Groq won), so we ignore the local Whisper result
            // to avoid sending a duplicate command.
            try { console.debug("Local Whisper finished but command was already resolved by Groq"); } catch (_) {}
          }
        } else {
          // Legacy/fallback fallback: handle directly if no commandId (shouldn't happen with our changes)
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

code = code.replace(oldWorkerMessage, newWorkerMessage);

const oldWorkerError = `      if (type === "error") {
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

const newWorkerError = `      if (type === "error") {
        addLog(\`❌ Whisper: \${message}\`, "error");
        if (mode === "command") {
          const commandId = e.data.commandId;
          if (commandId) {
            if (pendingCommandsRef.current.has(commandId)) {
              const resolveFn = pendingCommandsRef.current.get(commandId);
              resolveFn(""); // Resolve with empty to fail the isValidResult check and trigger throw
              pendingCommandsRef.current.delete(commandId);
            }
            // If it's not pending, do nothing (Groq already handled it)
          } else {
            resumeListeningRef.current();
          }
        }
        return;
      }`;

code = code.replace(oldWorkerError, newWorkerError);

fs.writeFileSync('src/SmartGlassesMiddleware/client/src/App.jsx', code);
