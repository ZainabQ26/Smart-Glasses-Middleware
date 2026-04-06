using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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

    private readonly Kernel _kernel;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AIService> _logger;
    private readonly string _advancedModelEndpoint;
    private readonly bool _useAdvancedModel;
    private readonly List<string> _knownTasks;
    private readonly ITaskDispatcher _dispatcher;

        public AIService(Kernel kernel, HttpClient httpClient, IConfiguration configuration, ILogger<AIService> logger, ITaskDispatcher dispatcher)
    {
        _kernel = kernel;
        _httpClient = httpClient;
        _logger = logger;
        if (configuration == null)
        {
            _advancedModelEndpoint = string.Empty;
            _useAdvancedModel = false;
        }
        else
        {
            _advancedModelEndpoint = configuration["AI:AdvancedModelEndpoint"] ?? string.Empty;
            try
            {
                _useAdvancedModel = configuration.GetValue<bool?>("AI:UseAdvancedModel") ?? false;
            }
            catch
            {
                _useAdvancedModel = false;
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

            // ── 2. Local-only classification (rule-based + keyword matching, no external AI) ──
            string detectedTask;
            try
            {
                var sw = Stopwatch.StartNew();
                detectedTask = await ClassifyOnlyAsync(input, cancellationToken);
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

            _logger.LogInformation("Task '{Task}' was not handled and advanced model is disabled; returning unrecognised response", detectedTask);
            return (detectedTask, "I didn't recognise that command. Please try rephrasing or enable the advanced model for open-ended requests.", false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request with AI service");
            return ("error", "An unexpected error occurred while processing your request. Please try again later.", false);
        }
    }

    private static readonly Dictionary<string, string> TaskDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["document-query"]     = "user wants to ask a question about the contents of a document",
        ["summarize-page"]     = "user wants a summary of the currently open or visible page in a document",
        ["highlight-word"]     = "user wants to highlight, search for, or find a word or phrase in the currently open document. Use when the user says 'highlight X', 'search for X', 'find X', or any variant of highlighting or searching text.",
        ["ocr"]                = "user wants to scan text, read text from an image, or perform OCR",
        ["ocr-retry"]          = "user wants to retry or redo an OCR scan",
        ["open-file"]          = "user wants to browse or choose from a list of recently uploaded files (shows 5 recent files to pick from). Use when the user wants to see options, pick a file, or browse recent uploads.",
        ["select-document"]    = "user wants to immediately open/preview the most recently uploaded document without browsing. Use when the user says 'select document', 'open last file', 'open latest document', 'select and open', or wants the last/most-recent document directly.",
        ["next-page"]          = "user wants to go to the next page in a document or PDF",
        ["previous-page"]      = "user wants to go to the previous page in a document or PDF",
        ["next-highlight"]     = "user wants to jump to the next highlighted match in a document or PDF",
        ["previous-highlight"] = "user wants to jump to the previous highlighted match in a document or PDF",
        ["open-admin-panel"]   = "user wants to open, navigate to, or access the admin panel or administration page",
        ["advanced"]           = "user wants advanced features, settings, or RAG document processing",
        ["template"]           = "user wants to use or fill in a template",
    };

    /// <summary>
    /// Classifies input entirely locally — no external AI call.
    /// Uses rule-based matching first, then keyword matching from registered handlers.
    /// Returns "unknown" when no local match is found (caller should route to advanced handler).
    /// </summary>
    public Task<string> ClassifyOnlyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return Task.FromResult("unknown");
        EnsureKnownTasksLoaded();

        // 1. Rule-based patterns (fastest, highest priority)
        var forcedTask = TryRuleBasedTask(input);
        if (!string.IsNullOrWhiteSpace(forcedTask) && _knownTasks.Contains(forcedTask))
        {
            return Task.FromResult(forcedTask);
        }

        // 2. Keyword matching via registered handler Keywords lists
        var keywordTask = _dispatcher.TryMatchByKeyword(input);
        if (!string.IsNullOrWhiteSpace(keywordTask) && _knownTasks.Contains(keywordTask))
        {
            return Task.FromResult(keywordTask);
        }

        // 3. No local match — caller routes to advanced handler if configured
        return Task.FromResult("unknown");
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

        // ── close-document ────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "close document",
            "close the document",
            "close pdf",
            "close the pdf",
            "close file",
            "exit document",
            "exit pdf",
            "exit the document"))
        {
            return "close-document";
        }

        // ── go-to-page ────────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "go to page",
            "jump to page",
            "navigate to page",
            "turn to page",
            "open page",
            "go to the page",
            "skip to page"))
        {
            return "go-to-page";
        }

        // ── go-to-loinc ───────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "loinc code",
            "go to loinc",
            "find loinc",
            "navigate to loinc",
            "open loinc")
            // Also match bare "loinc " followed by a code (e.g. "loinc 8480-6")
            || (normalized.Contains("loinc ") && normalized.IndexOf("loinc ", StringComparison.Ordinal) >= 0
                && normalized.Length > normalized.IndexOf("loinc ", StringComparison.Ordinal) + 6))
        {
            return "go-to-loinc";
        }

        // ── print-highlighted-pages (before print to avoid prefix clash) ──────
        if (ContainsAnyPhrase(normalized,
            "print highlighted pages",
            "print highlighted",
            "print highlighted page",
            "print the highlighted"))
        {
            return "print-highlighted-pages";
        }

        // ── print ─────────────────────────────────────────────────────────────
        // Note: "print-highlighted-pages" is checked above to avoid a prefix conflict.
        if (ContainsAnyPhrase(normalized,
            "print document",
            "print the document",
            "print pdf",
            "print the pdf",
            "print page",
            "print this",
            "print all",
            "print current"))
        {
            return "print";
        }

        // ── remove-highlights ─────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "remove highlights",
            "clear highlights",
            "delete highlights",
            "remove all highlights",
            "clear all highlights",
            "remove highlight",
            "clear highlight",
            "delete highlight"))
        {
            return "remove-highlights";
        }

        // ── select-document (open most-recent file directly) ──────────────────
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

        // ── open-file (browse list of recent files) ───────────────────────────
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

        // ── next-page ─────────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized, "next page", "page down", "go forward", "forward page"))
        {
            return "next-page";
        }

        // ── next-highlight ────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "next highlight",
            "next highlighted",
            "next highlighted word",
            "next highlighted phrase",
            "go to next highlight",
            "find next highlight"))
        {
            return "next-highlight";
        }

        // ── previous-page ─────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized, "previous page", "prev page", "back page", "page up", "go back a page"))
        {
            return "previous-page";
        }

        // ── previous-highlight ────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "previous highlight",
            "prev highlight",
            "back highlight",
            "previous highlighted",
            "previous highlighted word",
            "go to previous highlight",
            "find previous highlight"))
        {
            return "previous-highlight";
        }

        // ── summarize-page ────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "summarize this page",
            "summarize current page",
            "summarize page",
            "summary of this page",
            "summary of the page",
            "what is on this page",
            "summarise this page",
            "summarise page"))
        {
            return "summarize-page";
        }

        // ── document-query ────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "ask the document",
            "query document",
            "what does the document say",
            "summarize document",
            "summarise document",
            "explain in the document",
            "according to the document",
            "based on the document",
            "search document for",
            "ask document"))
        {
            return "document-query";
        }

        // ── document-highlight ────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "highlight in document",
            "document highlight",
            "highlight in the document"))
        {
            return "document-highlight";
        }

        // ── highlight-word (generic client-side PDF highlight / find) ──────────
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

        // ── ocr-retry (before ocr to avoid prefix clash) ──────────────────────
        if (ContainsAnyPhrase(normalized,
            "retry ocr",
            "redo ocr",
            "redo the ocr",
            "ocr retry",
            "try ocr again",
            "scan again"))
        {
            return "ocr-retry";
        }

        // ── ocr ───────────────────────────────────────────────────────────────
        if (ContainsAnyPhrase(normalized,
            "scan text",
            "read text from",
            "perform ocr",
            "run ocr",
            "do ocr",
            "ocr this",
            "scan image",
            "scan document",
            "scan the document",
            "extract text"))
        {
            return "ocr";
        }

        // ── open-admin-panel ──────────────────────────────────────────────────
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
            // Advanced model is used only when local classification yields "unknown"
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

        if (documentContext.ConversationContext != null && documentContext.ConversationContext.Messages.Any())
        {
            builder.AppendLine("Previous conversation context:");
            foreach (var message in documentContext.ConversationContext.Messages.OrderBy(m => m.Timestamp))
            {
                builder.AppendLine($"[{message.Role.ToUpper()}]: {message.Content}");
            }
            builder.AppendLine();
        }

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
