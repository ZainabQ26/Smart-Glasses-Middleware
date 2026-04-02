


using System.Text.Json;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public interface IAIService
{
    Task<(string task, string response, bool usedAdvancedModel)> ProcessRequestAsync(string input, CancellationToken cancellationToken = default, ProcessDocumentContext? documentContext = null);
    /// <summary>Classify only — returns the task name or "unknown". No response generated.</summary>
    Task<string> ClassifyOnlyAsync(string input, CancellationToken cancellationToken = default);
    /// <summary>Quick check whether the advanced model will be used for this input.</summary>
    Task<bool> WillUseAdvancedModelAsync(string input, CancellationToken cancellationToken = default);
}

public class AIService : IAIService
{
    private static readonly Dictionary<string, string> TaskSummaryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["time"] = "Returns the current date and time.",
        ["reminder"] = "Creates and saves a reminder.",
        ["delete-reminder"] = "Deletes a reminder that matches provided text.",
        ["edit-reminder"] = "Updates an existing reminder.",
        ["show-reminders"] = "Lists all saved reminders.",
        ["template"] = "Template/example task handler.",
        ["document-search"] = "Finds text in documents with location details.",
        ["document-highlight"] = "Highlights requested text in a document.",
        ["ocr"] = "Runs OCR for a document or image.",
        ["ocr-retry"] = "Retries OCR and embedding generation for a document.",
        ["document-query"] = "Answers semantic questions over documents (RAG).",
        ["summarize-page"] = "Summarizes the currently open PDF page.",
        ["advanced"] = "Routes complex requests to an external advanced model endpoint."
    };

    private readonly BedrockChatCompletionService _bedrockService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AIService> _logger;
    private readonly string _advancedModelEndpoint;
    private readonly bool _useAdvancedModel;
    private readonly bool _allowBedrockProcessing;
    private readonly List<string> _knownTasks;
    private readonly ITaskDispatcher _dispatcher;

        public AIService(BedrockChatCompletionService bedrockService, HttpClient httpClient, IConfiguration configuration, ILogger<AIService> logger, ITaskDispatcher dispatcher)
    {
        _bedrockService = bedrockService;
        _httpClient = httpClient;
        _logger = logger;
        if (configuration == null)
        {
            _advancedModelEndpoint = "http://localhost:5000/api/chat";
            _useAdvancedModel = false;
            _allowBedrockProcessing = false;
        }
        else
        {
            _advancedModelEndpoint = configuration["AI:AdvancedModelEndpoint"] ?? "http://localhost:5000/api/chat";
            try
            {
                _useAdvancedModel = configuration.GetValue<bool?>("AI:UseAdvancedModel") ?? false;
            }
            catch
            {
                _useAdvancedModel = false;
            }

            try
            {
                _allowBedrockProcessing = configuration.GetValue<bool?>("AI:AllowBedrockProcessing") ?? false;
            }
            catch
            {
                _allowBedrockProcessing = false;
            }
        }
        _dispatcher = dispatcher;
        // Do not eagerly enumerate all task handlers during construction
        // (those handlers may require heavy services like DB, storage, or cache)
        // Resolve known tasks lazily during request processing if needed
        _knownTasks = new List<string>();
    }

    public async Task<(string task, string response, bool usedAdvancedModel)> ProcessRequestAsync(string input, CancellationToken cancellationToken = default, ProcessDocumentContext? documentContext = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return ("unknown", "Please provide a request.", false);
            }

            EnsureKnownTasksLoaded();

            _logger.LogInformation("IAIService.ProcessRequestAsync entered; inputLength={L}", input.Length);

            // ── 1. Keyword-based routing (fastest path — no AI call needed) ──────
            var keywordTask = _dispatcher.TryMatchByKeyword(input);
            if (!string.IsNullOrWhiteSpace(keywordTask) && _knownTasks.Contains(keywordTask))
            {
                _logger.LogInformation("Keyword match: routing to task '{Task}' for input: {Input}", keywordTask, input);
                try
                {
                    var (kwHandled, kwResponse) = await _dispatcher.TryHandleAsync(keywordTask, input);
                    if (kwHandled) return (keywordTask, kwResponse ?? string.Empty, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Keyword-matched handler for task {Task} failed — falling through to AI", keywordTask);
                }
            }

            // ── 2. AI/rule-based classification ───────────────────────────────────
            // Classify with intent-aware prompt + normalization (reuse ClassifyOnlyAsync)
            string detectedTask;
            try
            {
                var sw = Stopwatch.StartNew();
                detectedTask = await ClassifyOnlyAsync(input, cancellationToken);
                var forcedTask = TryRuleBasedTask(input);
                if (!string.IsNullOrWhiteSpace(forcedTask)
                    && _knownTasks.Contains(forcedTask)
                    && !string.Equals(detectedTask, forcedTask, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Overriding detected task {Detected} -> {Forced} using rule-based routing for input: {Input}", detectedTask, forcedTask, input);
                    detectedTask = forcedTask;
                }
                sw.Stop();
                _logger.LogInformation("Detected task: {Task} for input: {Input} (taskDetectionMs={Ms})", detectedTask, input, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Task detection was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect task, defaulting to unknown");
                detectedTask = "unknown";
            }

            // Route anything not explicitly listed in known tasks to the advanced handler.
            if (detectedTask == "unknown" || !_knownTasks.Contains(detectedTask))
            {
                if (_useAdvancedModel)
                {
                    _logger.LogInformation("Task is not in known tasks, forwarding to advanced task handler");
                    try
                    {
                        var advancedInput = BuildAdvancedInput(input, documentContext);
                        var (handled, advancedResponse) = await _dispatcher.TryHandleAsync("advanced", advancedInput);

                        // Always return the advanced handler's message as-is.
                        if (handled && !string.IsNullOrWhiteSpace(advancedResponse))
                        {
                            return ("advanced", advancedResponse, true);
                        }

                        // If we reached here, the advanced handler didn't handle the request.
                        // The system is configured to use the advanced model, so treat this
                        // as an unavailable advanced model and return an error message
                        // instead of falling back to the local model.
                        _logger.LogWarning("Advanced model handler did not handle request; returning unavailable response (no fallback)");
                        return ("advanced", AdvancedTaskHandler.DispatcherFallbackMessage, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Advanced model call failed; returning unavailable response (no fallback)");
                        return ("advanced", AdvancedTaskHandler.DispatcherFallbackMessage, true);
                    }
                }
                else
                {
                    _logger.LogInformation("Advanced model is disabled by configuration; skipping advanced model and using local processing");
                }
            }

            // Try to invoke a concrete handler for the detected task via dispatcher
            try
            {
                var (handled, handlerResponse) = await _dispatcher.TryHandleAsync(detectedTask, input);
                if (handled) return (detectedTask, handlerResponse ?? string.Empty, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler for task {Task} failed", detectedTask);
            }

            // If the task wasn't handled locally:
            // - Prefer the advanced model if configured
            // - Otherwise, only fall back to the Bedrock/local model if expressly allowed
            if (_useAdvancedModel)
            {
                _logger.LogInformation("Delegating unhandled task to advanced model handler (detectedTask={Task})", detectedTask);
                try
                {
                    var advancedInput = BuildAdvancedInput(input, documentContext);
                    var (handledAdv, advancedResponse) = await _dispatcher.TryHandleAsync("advanced", advancedInput);
                    if (handledAdv && !string.IsNullOrWhiteSpace(advancedResponse))
                    {
                        return ("advanced", advancedResponse, true);
                    }

                    _logger.LogWarning("Advanced model handler did not handle request for task {Task}; returning unavailable response (no fallback)", detectedTask);
                    return ("advanced", AdvancedTaskHandler.DispatcherFallbackMessage, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Advanced model call failed for unhandled task {Task}; returning unavailable response (no fallback)", detectedTask);
                    return ("advanced", AdvancedTaskHandler.DispatcherFallbackMessage, true);
                }
            }

            if (!_allowBedrockProcessing)
            {
                _logger.LogInformation("Local Bedrock processing disabled; cannot process task {Task}", detectedTask);
                return (detectedTask, "Local model processing is disabled for this deployment. Please enable an advanced model to handle this request.", false);
            }

            // Process with local Bedrock model if allowed
            try
            {
                var processingPrompt = $@"
You are a helpful assistant processing a {detectedTask} task.

User request: {input}

Please provide a helpful and concise response:";

                _logger.LogDebug("Calling _bedrockService.GenerateTextAsync for processing prompt (detectedTask={Task})", detectedTask);
                var swProc = Stopwatch.StartNew();
                var response = await _bedrockService.GenerateTextAsync("You are a helpful assistant.", processingPrompt, cancellationToken);
                swProc.Stop();
                var result = response?.Trim();
                _logger.LogInformation("Generated local response (length={L}, ms={Ms})", result?.Length, swProc.ElapsedMilliseconds);
                return (detectedTask, result ?? "I couldn't process that locally.", false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Local model processing was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local model processing failed");
                return (detectedTask, "I'm currently unable to process your request. Please try again later.", false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request with AI service");
            return ("error", "An unexpected error occurred while processing your request. Please try again later.", false);
        }
    }

    private static readonly Dictionary<string, string> TaskDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["time"]               = "user wants to know the current time or date",
        ["reminder"]           = "user wants to set, add, or create a new reminder or alarm",
        ["edit-reminder"]      = "user wants to change or update an existing reminder",
        ["delete-reminder"]    = "user wants to remove, cancel, or delete a reminder",
        ["show-reminders"]     = "user wants to see, list, view, or show their reminders",
        ["document-query"]     = "user wants to ask a question about the contents of a document",
        ["summarize-page"]     = "user wants a summary of the currently open or visible page in a document",
        ["highlight-word"]     = "user wants to highlight, search for, or find a word or phrase in the currently open document. Use when the user says 'highlight X', 'search for X', 'find X', or any variant of highlighting or searching text.",
        ["ocr"]                = "user wants to scan text, read text from an image, or perform OCR",
        ["ocr-retry"]          = "user wants to retry or redo an OCR scan",
        ["open-file"]          = "user wants to browse or choose from a list of recently uploaded files (shows 5 recent files to pick from). Use when the user wants to see options, pick a file, or browse recent uploads.",
        ["select-document"]    = "user wants to immediately open/preview the most recently uploaded document without browsing. Use when the user says 'select document', 'open last file', 'open latest document', 'select and open', or wants the last/most-recent document directly.",
        ["next-page"]          = "user wants to go to the next page in a document or PDF",
        ["previous-page"]      = "user wants to go to the previous page in a document or PDF",
        ["open-admin-panel"]   = "user wants to open, navigate to, or access the admin panel or administration page",
        ["advanced"]           = "user wants advanced features, settings, or RAG document processing",
        ["template"]           = "user wants to use or fill in a template",
    };

    public async Task<string> ClassifyOnlyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        EnsureKnownTasksLoaded();

        var forcedTask = TryRuleBasedTask(input);
        if (!string.IsNullOrWhiteSpace(forcedTask) && _knownTasks.Contains(forcedTask))
        {
            return forcedTask;
        }

        var taskLines = _knownTasks
            .Select(t => TaskDescriptions.TryGetValue(t, out var desc)
                ? $"- {t}: {desc}"
                : $"- {t}")
            .ToList();

        var prompt = $@"You are a voice command classifier for smart glasses. Match the user's request to the most fitting task by INTENT, not exact wording.

Available tasks:
{string.Join("\n", taskLines)}

Rules:
- Respond with ONLY the task name (e.g. open-file). No explanation.
- If none match, respond with UNKNOWN.
- Interpret variations: ""can you open a file"" → open-file, ""what's the time"" → time, ""pick a document"" → select-document, etc.

User request: {input}
Task:";
        try
        {
            var systemMessage = "You are a task classifier. Only output the exact task string from the list provided.";
            var response = await _bedrockService.GenerateTextAsync(systemMessage, prompt, cancellationToken);
            var raw = response?.Trim().ToLowerInvariant() ?? "unknown";
            return NormalizeDetectedTask(raw);
        }
        catch { return "unknown"; }
    }

    private static bool ContainsAnyPhrase(string normalizedInput, params string[] phrases)
    {
        return phrases.Any(p => normalizedInput.Contains(p, StringComparison.Ordinal));
    }

    private string? TryRuleBasedTask(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var normalized = Regex.Replace(input.ToLowerInvariant(), @"[^a-z0-9\s-]", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        // "select document" family → auto-open the most recently uploaded file (no browsing)
        if (ContainsAnyPhrase(normalized,
            "select document",
            "select file",
            "select and open",
            "choose document",
            "choose file",
            "pick document",
            "pick file",
            "load document",
            "load file",
            "preview document",
            "preview the document",
            "preview last",
            "view document",
            "open last",
            "open the last",
            "open latest",
            "open the latest",
            "show last document",
            "show the last document",
            "show latest document",
            "view last",
            "latest document",
            "latest file",
            "most recent document",
            "most recent file",
            "last document",
            "recently uploaded document"))
        {
            return "select-document";
        }

        // "open file" family → show a browseable list of 5 recent files to pick from
        if (ContainsAnyPhrase(normalized,
            "open file",
            "open document",
            "open doc",
            "show recent",
            "show recent files",
            "recent files",
            "recent documents",
            "last uploaded",
            "uploaded documents",
            "show files",
            "show documents",
            "list files",
            "list documents",
            "browse files",
            "browse documents",
            "browse recent",
            "my files",
            "my documents",
            "see files",
            "see documents",
            "view files",
            "view recent",
            "file list",
            "document list",
            "all files",
            "all documents",
            "show me files",
            "show me documents",
            "show me my files",
            "show me my documents",
            "pick a file",
            "pick a document",
            "choose a file",
            "choose a document"))
        {
            return "open-file";
        }

        if (ContainsAnyPhrase(normalized, "next page", "page down"))
        {
            return "next-page";
        }

        if (ContainsAnyPhrase(normalized, "previous page", "prev page", "back page", "page up"))
        {
            return "previous-page";
        }

        if (ContainsAnyPhrase(normalized,
            "summarize this page",
            "summarize current page",
            "summarize page",
            "summary of this page",
            "summary of the page",
            "what is on this page"))
        {
            return "summarize-page";
        }

        // "highlight" / "search" / "find" commands → highlight word in the open PDF (client-side)
        if (ContainsAnyPhrase(normalized,
            "highlight ",
            "search for ",
            "search the ",
            "find the word",
            "find the phrase",
            "find "))
        {
            return "highlight-word";
        }

        // "open admin panel" / "go to admin" family → open the admin panel page
        if (ContainsAnyPhrase(normalized,
            "open admin",
            "admin panel",
            "go to admin",
            "show admin",
            "administration panel",
            "go to administration",
            "open administration"))
        {
            return "open-admin-panel";
        }

        return null;
    }

    public async Task<bool> WillUseAdvancedModelAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        try
        {
            var detected = await ClassifyOnlyAsync(input, cancellationToken);
            // If classification yields unknown and advanced model is enabled, we will delegate
            return string.Equals(detected, "unknown", StringComparison.OrdinalIgnoreCase) && _useAdvancedModel;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureKnownTasksLoaded()
    {
        if (_knownTasks.Count > 0) return;
        try
        {
            var uniqueKnownTasks = _dispatcher.KnownTasks
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct();
            _knownTasks.AddRange(uniqueKnownTasks);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load known tasks lazily");
        }
    }

    private static string BuildAdvancedInput(string input, ProcessDocumentContext? documentContext)
    {
        if (documentContext == null || documentContext.Pages.Count == 0)
        {
            return input;
        }

        var builder = new StringBuilder();
        builder.AppendLine("User request:");
        builder.AppendLine(input);
        builder.AppendLine();
        builder.AppendLine("Open document context:");
        builder.AppendLine($"- Document: {documentContext.DocumentName ?? "Unknown document"}");

        if (documentContext.CurrentPage.HasValue)
        {
            builder.AppendLine($"- Current page: {documentContext.CurrentPage.Value}");
        }

        if (documentContext.TotalPages.HasValue)
        {
            builder.AppendLine($"- Total pages: {documentContext.TotalPages.Value}");
        }

        if (documentContext.Highlights.Count > 0)
        {
            builder.AppendLine("- Active highlights:");
            foreach (var highlight in documentContext.Highlights.Where(h => !string.IsNullOrWhiteSpace(h.Keyword)))
            {
                var pageList = highlight.Pages.Count > 0 ? string.Join(", ", highlight.Pages) : "none";
                builder.AppendLine($"  - {highlight.Keyword} (pages: {pageList})");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Full document text:");
        foreach (var page in documentContext.Pages.Where(p => !string.IsNullOrWhiteSpace(p.Text)).OrderBy(p => p.PageNumber))
        {
            builder.AppendLine();
            builder.AppendLine($"[Page {page.PageNumber}]");
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private string NormalizeDetectedTask(string rawTask)
    {
        if (string.IsNullOrWhiteSpace(rawTask)) return "unknown";
        var raw = rawTask.Trim().ToLowerInvariant();
        if (_knownTasks.Count == 0)
        {
            return raw;
        }

        if (_knownTasks.Contains(raw)) return raw;

        var normalized = raw.Replace(" ", "-");
        if (_knownTasks.Contains(normalized)) return normalized;

        var partial = _knownTasks.FirstOrDefault(t =>
            t == normalized || normalized.StartsWith(t, StringComparison.OrdinalIgnoreCase) || t.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));

        return partial ?? "unknown";
    }

}
