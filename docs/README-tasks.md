# Task Handlers - Local AI Tasks
===============================

## Overview
This folder contains all task handlers that execute locally. Any task handler placed in this folder will be compiled and loaded at runtime, making them easy to edit and customize without rebuilding the main application.

**Architecture:**
- ✅ Tasks in `tasks/` folder → Registered and dispatched by the task system
- ✅ Classification is done **entirely locally** — no external AI call for known commands
- ❌ Unknown tasks → Route to AWS Bedrock via `AdvancedTaskHandler` (only when `AI:UseAdvancedModel` is `true`)

The application automatically:
1. Compiles `.cs` files in this folder at startup
2. Loads `.dll` plugin files from this folder
3. Registers all `ITaskHandler` implementations with dependency injection
4. Classifies commands **locally** using rule-based pattern matching + keyword lists (no AI call)
5. Routes unrecognised commands to the advanced handler (AWS Bedrock) if configured

## How Classification Works (Local-Only, No External AI)

Command recognition is entirely local and happens in two stages:

### Stage 1 — Keyword matching (fastest path)
Each handler declares a `Keywords` list. The dispatcher scans those lists against the transcript before any other logic runs. Example: "show recent files" matches `OpenFileTaskHandler.Keywords`.

### Stage 2 — Rule-based pattern matching
`TryRuleBasedTask` in `AIService` runs an extensive set of phrase patterns for all known tasks. This covers natural-language variations that keyword lists might miss (e.g. "latest document" → `select-document`, "print this" → `print`, "retry ocr" → `ocr-retry`).

### Stage 3 — Advanced model (external AI, opt-in)
If both stages produce no match, the input is classified as `unknown`. When `AI:UseAdvancedModel = true`, it is forwarded to `AdvancedTaskHandler` which calls AWS Bedrock. **This is the only point where an external AI is ever called.**

> **Cost note:** All routine voice commands are handled at stage 1 or 2 with zero API calls. AWS Bedrock is only charged when a genuinely unrecognised command reaches the advanced handler.

## Available Task Handlers

### Core Local Tasks

**TemplateTaskHandler.cs** - `task: template`
- Template/example for creating new handlers

### Navigation Tasks

**NextPageTaskHandler.cs** - `task: next-page` — "next page", "page down"  
**PreviousPageTaskHandler.cs** - `task: previous-page` — "previous page", "page up"  
**GoToPageTaskHandler.cs** - `task: go-to-page` — "go to page 5", "jump to page 10"  
**GoToLoincPageTaskHandler.cs** - `task: go-to-loinc` — "loinc 8480-6", "go to loinc code"  

### Document Tasks

**OpenFileTaskHandler.cs** - `task: open-file` — "show recent files", "browse documents"  
**SelectDocumentTaskHandler.cs** - `task: select-document` — "open the latest document", "last file"  
**CloseDocumentTaskHandler.cs** - `task: close-document` — "close document", "exit pdf"  
**SummarizePageTaskHandler.cs** - `task: summarize-page` — "summarize this page"  
**HighlightWordTaskHandler.cs** - `task: highlight-word` — "highlight headache", "find dosage"  
**RemoveHighlightsTaskHandler.cs** - `task: remove-highlights` — "clear all highlights"  
**DocumentSearchTaskHandler.cs** - `task: document-search` — "search for contraindications"  
**DocumentHighlightTaskHandler.cs** - `task: document-highlight` — "highlight in document"  
**DocumentQueryTaskHandler.cs** - `task: document-query` — "what does the document say about dosage"  

### Print / OCR Tasks

**PrintTaskHandler.cs** - `task: print` — "print document", "print this"  
**PrintHighlightedPagesTaskHandler.cs** - `task: print-highlighted-pages` — "print highlighted pages"  
**OcrTaskHandler.cs** (via `ocr` task) — "scan text", "perform ocr"  

### Administrative Tasks

**AdminPanelTaskHandler.cs** - `task: open-admin-panel` — "open admin panel", "go to admin"  

### Advanced Model (External AI — AWS Bedrock)

**AdvancedTaskHandler.cs** - `task: advanced`
- Calls AWS Bedrock for open-ended or unrecognised requests
- Only invoked when local classification returns `unknown` **and** `AI:UseAdvancedModel = true`
- Configured via `AWS:Bedrock:AdvancedModelId` in appsettings.json

## Voice Recognition Pipeline

```
Microphone → Groq Whisper API (transcription) → activation word check ("Iris")
    → transcript → POST /api/process
        → Stage 1: keyword match (handler.Keywords)
        → Stage 2: rule-based match (TryRuleBasedTask)
        → Stage 3: advanced handler [AWS Bedrock — ONLY for unknown commands]
```

## Audio Transcription via Groq Whisper API

### Overview
The system uses **Groq's Whisper API** for real-time speech-to-text transcription. Groq offers ultra-fast inference on OpenAI's Whisper model, making it ideal for low-latency voice command processing on smart glasses.

### How It Works

**Frontend (React/WebAudio API):**
1. Audio is captured from the device microphone in real-time via `AudioContext`
2. Audio data is buffered in 32-bit float format at 16kHz sample rate
3. **Two transcription paths:**
   - **Wake-word detection** (wakeword path): Smaller audio chunks (~500ms) sent frequently for faster activation word recognition
   - **Command transcription** (command path): Larger audio chunks (~2s) accumulated after wake-word is detected

**Backend (ASP.NET Core → Groq API):**
1. Client sends audio WAV blob to `/api/process/transcribe` endpoint
2. Server forwards audio to Groq Whisper API with model specification
3. Groq performs ultra-fast inference (typically 100-500ms for real-time audio)
4. Server receives transcribed text and returns to client
5. Client applies blank audio/noise detection before sending to task classifier

### Models Used

| Context | Model | Speed | Accuracy | Use Case |
|---------|-------|-------|----------|----------|
| Wake-word detection | `whisper-large-v3-turbo` | Fastest (10-200ms) | High | Rapid "Hello Iris" detection |
| Command transcription | `whisper-large-v3` | Fast (100-500ms) | Very High | Full command accuracy |

The **turbo variant** is used for wake-word paths (faster response) while the standard **large** model is used for command transcription (higher accuracy for complex commands).

### Request Flow Example

```
User speaks: "Hello Iris, go to page 5"

1. Microphone capture (WebAudio API)
   ↓
2. Audio buffering in 32-bit float chunks
   ↓
3a. First chunk → POST /api/process/transcribe (wakeword=true, model=whisper-turbo)
   → Groq returns: "Hello Iris"
   → Wake-word detected, enter command mode
   ↓
3b. Next chunks → POST /api/process/transcribe (wakeword=false, model=whisper-large)
   → Groq returns: "go to page 5"
   → Run blank audio detection
   ↓
4. POST /api/process with transcript
   → Stage 1: Match "go-to-page" via keywords
   → Dispatch to GoToPageTaskHandler
```

### Configuration

**Groq API Credentials:**
```json
{
  "AI": {
    "Groq": {
      "ApiKey": "gsk_your_api_key_here",
      "WhisperEndpoint": "https://api.groq.com/openai/v1/audio/transcriptions",
      "WhisperWakewordModel": "whisper-large-v3-turbo",
      "WhisperCommandModel": "whisper-large-v3"
    }
  }
}
```

> **⚠️ SECURITY WARNING:** Never commit the actual API key to `appsettings.json` or any config file. The example above shows the structure only.

**Never store the actual Groq API key in appsettings.json or any configuration file committed to git.**

Instead, inject it as an **environment variable**:

#### Local Development
```bash
# Set before running the app
export AI__Groq__ApiKey="gsk_your_actual_key"
dotnet run
```

Or in `.env` file (for docker-compose):
```
AI__Groq__ApiKey=gsk_your_actual_key
```

#### GitHub Actions (CI/CD)

1. **Store API key in GitHub Secrets:**
   - Go to repository Settings → Secrets and Variables → Actions
   - Create a new secret: `GROQ_API_KEY` with your actual key
   
2. **Reference in workflow:**
```yaml
# .github/workflows/deploy.yml
env:
  AI__Groq__ApiKey: ${{ secrets.GROQ_API_KEY }}
```

GitHub Actions **automatically masks secrets in logs** — if the Groq key appears anywhere in logs, it will be redacted as `***`.

#### AWS ECS (Production)

Secrets are injected via AWS Secrets Manager (same pattern as DB credentials):

```hcl
# terraform configuration
taskDefinition = {
  containerDefinitions = [{
    secrets = [
      {
        name      = "AI__Groq__ApiKey"
        valueFrom = aws_secretsmanager_secret_version.groq_key.arn
      }
    ]
  }]
}
```

Get an API key from [Groq Console](https://console.groq.com).

**Groq Timeout Settings:**
```json
{
  "ServiceTimeouts": {
    "TranscriptionTimeoutSeconds": 30
  }
}
```

Default: 30 seconds. Increase if experiencing timeout errors with slow network.

### Cost & Performance Notes

**Speed:**
- Wake-word detection: 10-200ms (Groq's strength is ultra-low latency)
- Command transcription: 100-500ms per audio chunk
- No local fallback if Groq is unavailable (requires API key)

**Cost:**
- Groq pricing is per-request, typically $0.02-0.05 per transcription (no monthly minimum)
- Smart caching of credentials means no per-request authentication overhead
- Significantly cheaper than AWS Transcribe or Google Cloud Speech-to-Text at scale

**Reliability:**
- Automatic retry on transient failures (built into HTTP client)
- Graceful error messages if API is unavailable
- No audio is cached on-device after transcription

### Troubleshooting

| Issue | Solution |
|-------|----------|
| "Transcription timeout" | Increase `TranscriptionTimeoutSeconds` or check network latency |
| Wake-word not detected | Verify `ActivationWord` config, ensure microphone is working, check Groq API key |
| High latency (>1s) | Groq servers may be experiencing load; add retry logic or use smaller chunks |
| API key invalid | Generate new key from [Groq Console](https://console.groq.com), ensure no whitespace |

## Configuration

**Activation word (configurable):**
```json
{
  "AI": {
    "ActivationWord": "Iris"
  }
}
```

**Voice transcription (Groq Whisper only):**
```json
{
  "AI": {
    "SpeechTranscriptionMode": "groq-only",
    "Groq": {
      "ApiKey": "gsk_...",
      "WhisperModel": "whisper-large-v3-turbo",
      "WhisperEndpoint": "https://api.groq.com/openai/v1/audio/transcriptions"
    }
  }
}
```

**Known Tasks (controls which tasks the local classifier considers):**
```json
{
  "AI": {
    "KnownTasks": [
      "template",
      "open-file", "select-document", "next-page", "previous-page", "go-to-page",
      "go-to-loinc", "close-document", "highlight-word", "remove-highlights",
      "summarize-page", "document-search", "document-highlight", "document-query",
      "print", "print-highlighted-pages", "ocr", "ocr-retry",
      "open-admin-panel", "advanced"
    ]
  }
}
```

**Advanced model (AWS Bedrock — external AI, opt-in):**
```json
{
  "AI": {
    "UseAdvancedModel": true
  },
  "AWS": {
    "Bedrock": {
      "AdvancedModelId": "anthropic.claude-3-5-sonnet-20241022-v2:0"
    }
  }
}
```

## File Storage (S3 — no separate metadata database)

Files are stored in S3/MinIO. **No separate database is needed for file metadata.**  
On every server start, `S3FileIndexService` scans the bucket, reads object user-defined metadata (`sgm-file-id`, `sgm-filename`, `sgm-label`) and rebuilds the in-memory `ManagedFile` index automatically.

Each uploaded file carries the metadata needed for reconstruction:
```
x-amz-meta-sgm-file-id    → application-level Guid
x-amz-meta-sgm-filename   → original file name
x-amz-meta-sgm-label      → user-friendly label
```

This eliminates the previous `StatePersistenceService` save-on-shutdown approach and reduces cost: no RDS/database instance is required for file tracking.

## Creating Custom Tasks

### Option 1: Edit .cs files directly (Recommended)

1. **Copy TemplateTaskHandler.cs** to a new file
2. **Change the class name** and `Name` property
3. **Add keywords** to the `Keywords` list for fast keyword-based matching
4. **Add rule-based patterns** to `TryRuleBasedTask` in `AIService.cs` if needed
5. **Implement your logic** in `HandleAsync`
6. **Restart the application** - it will auto-compile and load

Example:
```csharp
public class WeatherTaskHandler : ITaskHandler
{
    public string Name => "weather";
    public IReadOnlyList<string> Keywords => new[] { "weather", "forecast", "temperature" };
    
    public async Task<string> HandleAsync(string input)
    {
        // Your local logic here — no external AI needed
        return "Weather information...";
    }
}
```

### Option 2: Build as plugin DLL

1. **Create a class library**: `dotnet new classlib -n MyTaskPlugin`
2. **Add ITaskHandler interface** (copy from main project)
3. **Implement handler** with unique `Name`
4. **Build and copy DLL** to `tasks/` folder
5. **Restart application**

## Dependencies for Handlers

Handlers can inject any registered service:
- `ILogger<T>` - Logging
- `ApplicationDbContext` - Database access
- `IConfiguration` - Configuration
- `IHttpClientFactory` - HTTP calls
- `IMediaStorageService` - S3/MinIO file access

## Troubleshooting

**Handler not loading:**
- Check logs for compilation errors
- Ensure `Name` property is unique
- Verify namespace is `SmartGlassesMiddleware.Services`
- Restart application after changes

**Command routes to advanced model unexpectedly:**
- Check `GET /api/debug/tasks/status` to see which handlers are registered
- Add keywords to the handler's `Keywords` list
- Or add phrase patterns to `TryRuleBasedTask` in `AIService.cs`

**File not appearing after restart:**
- Ensure the file was uploaded with `sgm-*` metadata (all controllers do this automatically)
- Check S3FileIndexService startup logs for errors
- Files without metadata are still indexed using the S3 key path

## Best Practices

1. **Keep tasks simple** - Each handler should do one thing well
2. **Use descriptive names** - Make task names intuitive ("document-search" not "ds")
3. **Prefer Keywords** - Keyword matching is O(1) and runs before rule-based patterns
4. **Handle errors gracefully** - Always catch exceptions and return user-friendly messages
5. **No external AI for known tasks** - Use local logic; external AI is reserved for the advanced handler