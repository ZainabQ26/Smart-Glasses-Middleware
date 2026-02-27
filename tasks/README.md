# Task Handlers - Local AI Tasks
===============================

## Overview
This folder contains all task handlers that execute locally. Any task handler placed in this folder will be compiled and loaded at runtime, making them easy to edit and customize without rebuilding the main application.

**Architecture:**
- ✅ Tasks in `tasks/` folder → Registered and dispatched by the task system
- ❌ Unknown tasks → Route to external REST API (advanced model, if configured)

The application automatically:
1. Compiles `.cs` files in this folder at startup
2. Loads `.dll` plugin files from this folder
3. Registers all `ITaskHandler` implementations with dependency injection
4. Uses the configured AI model (OpenAI-compatible endpoint locally, or AWS Bedrock in production) to classify requests and route to appropriate handlers

## Available Task Handlers

### Core Local Tasks

**TimeTaskHandler.cs** - `task: time`
- Returns current date and time
- Simple local task with no external dependencies

**ReminderTaskHandler.cs** - `task: reminder`
- Saves reminders to database
- Local task with database persistence

**DeleteReminderTaskHandler.cs** - `task: delete-reminder`
- Deletes an existing reminder from the database by matching text
- Usage: "delete my reminder about [text]" or "remove [text] reminder"

**EditReminderTaskHandler.cs** - `task: edit-reminder`
- Updates the text of an existing reminder
- Usage: "change my reminder about [old text] to [new text]"

**TemplateTaskHandler.cs** - `task: template`
- Template/example for creating new handlers
- Copy and modify to create custom tasks

### Document Processing Tasks

**DocumentSearchTaskHandler.cs** - `task: document-search`
- Finds text in documents and returns exact locations
- Returns page numbers, positions, and context
- Usage: "find [text] in document [filename]"

**DocumentHighlightTaskHandler.cs** - `task: document-highlight`
- Highlights specific text in documents
- Returns highlighted copy without modifying original
- Usage: "highlight [text] in document [id]"

**OCRTaskHandler.cs** - `task: ocr`
- OCR (Optical Character Recognition) for images
- Reprocesses a document and regenerates embeddings when given a document ID
- Full OCR requires Tesseract to be configured
- Usage: "ocr [document-id]"

**OCRRetryTaskHandler.cs** - `task: ocr-retry`
- Explicitly retries OCR and embedding generation for an existing document
- Usage: provide a document ID (GUID)

**DocumentQueryTaskHandler.cs** - `task: document-query`
- RAG-based intelligent document queries
- Searches documents semantically and provides answers
- Usage: "What does the document say about [topic]?"

### Advanced Model (External REST API)

**AdvancedTaskHandler.cs** - `task: advanced`
- Routes complex requests to an external REST API endpoint
- Configured via `AI:AdvancedModelEndpoint` in appsettings.json
- Automatically used for unknown tasks or complex queries when `AI:UseAdvancedModel` is `true`

## How It Works

1. **User sends request** → "What time is it?"
2. **AI model detects task** → Classifies as "time" task
3. **System checks handlers** → Finds TimeTaskHandler in tasks/
4. **Executes handler** → Processes the request
5. **Returns response** → "The current time is 2:30 PM..."

If no handler matches:
1. **Unknown task detected**
2. **Routes to AdvancedTaskHandler** (if `AI:UseAdvancedModel` is `true`)
3. **Calls external REST API** (configured via `AI:AdvancedModelEndpoint`)
4. **Returns advanced model response**

## Creating Custom Tasks

### Option 1: Edit .cs files directly (Recommended)

1. **Copy TemplateTaskHandler.cs** to a new file
2. **Change the class name** and `Name` property
3. **Implement your logic** in `HandleAsync`
4. **Restart the application** - it will auto-compile and load

Example:
```csharp
public class WeatherTaskHandler : ITaskHandler
{
    public string Name => "weather";
    
    public async Task<string> HandleAsync(string input)
    {
        // Your logic here
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

## Configuration

**Known Tasks (controls which tasks the AI classifier considers):**
```json
{
  "AI": {
    "KnownTasks": [ "time", "reminder", "edit-reminder", "delete-reminder", "template" ]
  }
}
```

**Advanced Model Endpoint:**
```json
{
  "AI": {
    "UseAdvancedModel": true,
    "AdvancedModelEndpoint": "https://your-advanced-api/endpoint"
  }
}
```

## Dependencies for Handlers

Handlers can inject any registered service:
- `ILogger<T>` - Logging
- `ApplicationDbContext` - Database access
- `IConfiguration` - Configuration
- `IHttpClientFactory` - HTTP calls
- `IRAGService` - Document queries
- `IDocumentProcessingService` - Document processing
- `IDocumentHighlightService` - Document highlighting

Example with dependencies:
```csharp
public class MyTaskHandler : ITaskHandler
{
    private readonly ILogger<MyTaskHandler> _logger;
    private readonly ApplicationDbContext _context;
    
    public MyTaskHandler(ILogger<MyTaskHandler> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }
    
    public string Name => "my-task";
    
    public async Task<string> HandleAsync(string input)
    {
        _logger.LogInformation("Processing: {Input}", input);
        // Your logic here
        return "Response";
    }
}
```

## Troubleshooting

**Handler not loading:**
- Check logs for compilation errors
- Ensure `Name` property is unique
- Verify namespace is `SmartGlassesMiddleware.Services`
- Restart application after changes

**Duplicate handler names:**
- Each handler must have a unique `Name` (case-insensitive)
- Check both `.cs` files and `.dll` plugins

**Dependencies not injecting:**
- Ensure service is registered in `Program.cs`
- Use correct constructor injection pattern

## Best Practices

1. **Keep tasks simple** - Each handler should do one thing well
2. **Use descriptive names** - Make task names intuitive ("document-search" not "ds")
3. **Log extensively** - Help debugging with good logging
4. **Handle errors gracefully** - Always catch exceptions and return user-friendly messages
5. **Document usage** - Add comments explaining input format
6. **Test locally** - Iterate quickly using a local OpenAI-compatible endpoint

## Examples of Good vs Bad Task Names

✅ Good:
- "document-search" - Clear what it does
- "weather-forecast" - Descriptive
- "translate-text" - Action-based

❌ Bad:
- "ds" - Too abbreviated
- "handler1" - Not descriptive
- "do-stuff" - Too vague