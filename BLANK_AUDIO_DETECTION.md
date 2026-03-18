# Blank Audio and Background Noise Detection

## Overview
Implemented automatic detection and cancellation of requests that originate from blank audio, background chatter, or speech that doesn't form coherent commands. Instead of forwarding these to the AI/Advanced Task Handler, the system now cancels the request and prompts the user to try again.

## Detection Logic
The `isBlankAudioOrNoise()` helper function in `src/SmartGlassesMiddleware/client/src/App.jsx` detects:

### 1. **Empty or Very Short Text**
- Empty strings or text with fewer than 3 characters
- Catches cases where Whisper returns minimal transcription

### 2. **Background Noise Interjections**
- Repeated filler words: "uh", "um", "ah", "oh", "hmm", "er", etc.
- Stands-alone exclamations: "yeah", "yep", "nope", "huh", "shh", etc.
- Sound effects: "pfft", "tsk", "brr", "ggs"

### 3. **Repeated Characters**
- Long vowel sequences: "aaaa", "uuuu", "oooo"
- Repeated consonants: "bbbbb", "dddd"
- Single letters separated by spaces

### 4. **Filler Word Dominance**
- If more than 50% of words are filler words, the text is rejected
- Filler words include: um, uh, ah, oh, hmm, er, erm, like

### 5. **Gibberish Patterns**
- Text with extreme vowel-to-consonant imbalance
- More than 80% vowels or more than 80% consonants suggests unintelligible audio

## Integration Point
The detection occurs in the `sendToAI()` function in `App.jsx`, immediately after text normalization:

```javascript
// Check for blank audio or background noise
if (isBlankAudioOrNoise(commandText)) {
  addLog("🔊 Detected blank audio or background noise — cancelling request.", "warn");
  setPendingAction(null);
  showAiResponse("I didn't hear a clear command. Please try again.");
  resumeListeningRef.current?.();
  return;
}
```

## Behavior When Detected
1. **Logging**: A warning message is logged indicating blank audio/noise was detected
2. **User Feedback**: Shows "I didn't hear a clear command. Please try again."
3. **State Management**: Clears any pending actions
4. **Resumption**: Automatically resumes listening in wake-word mode without requiring re-activation

## Request Flow

### Before Implementation
```
Blank Audio → Whisper → sendToAI → AI Processing → Advanced Handler Fallback
```

### After Implementation
```
Blank Audio → Whisper → sendToAI → [DETECTED] → Cancel & Resume Listening
             (no API call, no AI processing)
```

## Testing Recommendations
Test the following scenarios to verify proper detection:

1. **Empty Audio**: Mic records but user says nothing
2. **Filler Words**: User says "uh um ah"
3. **Chatter**: User says "yeah yeah yeah"
4. **Repeated Sounds**: User says "aaaaaaa" or "bbbbb"
5. **Meaningful Commands**: Ensure normal commands like "go to page 5" still work
6. **Mixed Filler**: User says "um um search something" (should work since "search something" is meaningful)

## Performance Impact
- Minimal: Detection is synchronous string analysis
- No network requests are made for detected noise
- Reduces unnecessary API calls and AI processing for garbage input

## Configuration
The detection patterns are hardcoded in `isBlankAudioOrNoise()`. To adjust sensitivity:

1. **Less Sensitive**: Remove patterns from `noisePatterns` array or relax regexes
2. **More Sensitive**: Add new patterns or lower the filler word threshold from 0.5

## Related Files
- `src/SmartGlassesMiddleware/client/src/App.jsx` - Main detection logic
- `src/SmartGlassesMiddleware/client/src/whisperWorker.js` - Whisper transcription
- `tasks/AdvancedTaskHandler.cs` - Fallback handler (no longer invoked for noise)
