Task Handlers - Local AI Tasks
===============================

Overview
--------
This folder contains ALL task handlers that execute locally using Ollama. Any task handler placed in this folder will be compiled and loaded at runtime, making them easy to edit and customize without rebuilding the main application.

**Architecture:**
- ✅ Tasks in `tasks/` folder → Execute locally with Ollama
- ❌ Tasks NOT in `tasks/` folder → Route to external REST API (advanced model)

The application automatically:
1. Compiles `.cs` files in this folder at startup
2. Loads `.dll` plugin files from this folder
3. Registers all `ITaskHandler` implementations with dependency injection
4. Uses Ollama to classify requests and route to appropriate handlers

Available Task Handlers
------------------------

### Core Local Tasks (Ollama-based)

**TimeTaskHandler.cs** - `task: time`
- Returns current date and time
- Simple local task with no external dependencies

**ReminderTaskHandler.cs** - `task: reminder`
- Saves reminders to database
- Local task with database persistence

**TemplateTaskHandler.cs** - `task: template`
- Template/example for creating new handlers
- Copy and modify to create custom tasks

### Document Processing Tasks (Local)

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
- Placeholder implementation (requires Tesseract setup)
- Usage: "ocr [document-id]"

**DocumentQueryTaskHandler.cs** - `task: document-query`
- RAG-based intelligent document queries
- Searches documents semantically and provides answers
- Usage: "What does the document say about [topic]?"

### Advanced Model (External REST API)

**AdvancedTaskHandler.cs** - `task: advanced`
- Routes complex requests to external REST API
- Configured via `AI:AdvancedModelEndpoint` in appsettings.json
- Automatically used for unknown tasks or complex queries

How It Works
------------

1. **User sends request** → "What time is it?"
2. **Ollama detects task** → Classifies as "time" task
3. **System checks handlers** → Finds TimeTaskHandler in tasks/
4. **Executes locally** → Uses Ollama to process
5. **Returns response** → "The current time is 2:30 PM..."

If no handler matches:
1. **Unknown task detected**
2. **Routes to AdvancedTaskHandler**
3. **Calls external REST API** (e.g., GPT-4, Claude)
4. **Returns advanced model response**

Creating Custom Tasks
---------------------

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

Configuration
-------------

**Enable/Disable Handlers:**
Edit `appsettings.json`:
```json
{
  "AI": {
    "KnownTasks": ["time", "reminder", "document-search", "document-highlight", "ocr", "document-query", "advanced"]
  }
}
```

**Advanced Model Endpoint:**
```json
{
  "AI": {
    "AdvancedModelEndpoint": "https://api.openai.com/v1/chat/completions",
    "AdvancedModelName": "gpt-4"
  }
}
```

Dependencies for Handlers
--------------------------

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

Troubleshooting
---------------

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

Best Practices
--------------

1. **Keep tasks simple** - Each handler should do one thing well
2. **Use descriptive names** - Make task names intuitive ("document-search" not "ds")
3. **Log extensively** - Help debugging with good logging
4. **Handle errors gracefully** - Always catch exceptions and return user-friendly messages
5. **Document usage** - Add comments explaining input format
6. **Test locally** - Use Ollama for fast iteration

Examples of Good vs Bad Task Names
-----------------------------------

✅ Good:
- "document-search" - Clear what it does
- "weather-forecast" - Descriptive
- "translate-text" - Action-based

❌ Bad:
- "ds" - Too abbreviated
- "handler1" - Not descriptive
- "do-stuff" - Too vague

Migration from Services Folder
-------------------------------

**OLD Architecture** (deprecated):
- Handlers in `src/SmartGlassesMiddleware/Services/`
- Required rebuild to add new handlers
- Harder to customize

**NEW Architecture** (current):
- ALL handlers in `tasks/` folder
- Auto-compiled at runtime
- Easy to edit and customize
- Local tasks vs external API routing

If you have old handlers in Services/, they should be moved to tasks/ folder.
