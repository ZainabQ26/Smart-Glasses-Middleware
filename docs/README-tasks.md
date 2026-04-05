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
Each handler declares a `Keywords` list. The dispatcher scans those lists against the transcript before any other logic runs. Example: "what time is it" matches `TimeTaskHandler.Keywords`.

### Stage 2 — Rule-based pattern matching
`TryRuleBasedTask` in `AIService` runs an extensive set of phrase patterns for all known tasks. This covers natural-language variations that keyword lists might miss (e.g. "latest document" → `select-document`, "print this" → `print`, "retry ocr" → `ocr-retry`).

### Stage 3 — Advanced model (external AI, opt-in)
If both stages produce no match, the input is classified as `unknown`. When `AI:UseAdvancedModel = true`, it is forwarded to `AdvancedTaskHandler` which calls AWS Bedrock. **This is the only point where an external AI is ever called.**

> **Cost note:** All routine voice commands are handled at stage 1 or 2 with zero API calls. AWS Bedrock is only charged when a genuinely unrecognised command reaches the advanced handler.

## Available Task Handlers

### Core Local Tasks

**TimeTaskHandler.cs** - `task: time`
- Returns current date and time locally (no external call)
- Example: "what time is it", "what's today's date"

**ReminderTaskHandler.cs** - `task: reminder`
- Saves reminders to the in-memory database
- Example: "remind me to call John at 3pm", "set a reminder for tomorrow"

**DeleteReminderTaskHandler.cs** - `task: delete-reminder`
- Deletes a reminder by matching text
- Example: "delete my reminder about the meeting", "cancel the dentist reminder"

**EditReminderTaskHandler.cs** - `task: edit-reminder`
- Updates an existing reminder
- Example: "change the dentist reminder to 4pm"

**ShowRemindersTaskHandler.cs** - `task: show-reminders`
- Lists all saved reminders
- Example: "show my reminders", "what are my reminders"

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
      "time", "reminder", "edit-reminder", "delete-reminder", "show-reminders",
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