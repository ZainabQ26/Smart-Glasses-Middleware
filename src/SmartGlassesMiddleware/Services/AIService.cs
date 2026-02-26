using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;
using System.Linq;
using System.Diagnostics;

namespace SmartGlassesMiddleware.Services;

public interface IAIService
{
    Task<(string task, string response, bool usedAdvancedModel)> ProcessRequestAsync(string input, CancellationToken cancellationToken = default);
    /// <summary>Classify only — returns the task name or "unknown". No response generated.</summary>
    Task<string> ClassifyOnlyAsync(string input, CancellationToken cancellationToken = default);
    /// <summary>Quick check whether the advanced model will be used for this input.</summary>
    Task<bool> WillUseAdvancedModelAsync(string input, CancellationToken cancellationToken = default);
}

public class OllamaAIService : IAIService
{
    private static readonly Dictionary<string, string> TaskSummaryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["time"] = "Returns the current date and time.",
        ["reminder"] = "Creates and saves a reminder.",
        ["delete-reminder"] = "Deletes a reminder that matches provided text.",
        ["edit-reminder"] = "Updates an existing reminder.",
        ["template"] = "Template/example task handler.",
        ["document-search"] = "Finds text in documents with location details.",
        ["document-highlight"] = "Highlights requested text in a document.",
        ["ocr"] = "Runs OCR for a document or image.",
        ["ocr-retry"] = "Retries OCR and embedding generation for a document.",
        ["document-query"] = "Answers semantic questions over documents (RAG).",
        ["advanced"] = "Routes complex requests to an external advanced model endpoint."
    };

    private readonly Kernel _kernel;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaAIService> _logger;
    private readonly string _advancedModelEndpoint;
    private readonly string _advancedModelName;
    private readonly bool _useAdvancedModel;
    private readonly List<string> _knownTasks;
    private readonly ITaskDispatcher _dispatcher;

    public OllamaAIService(Kernel kernel, HttpClient httpClient, IConfiguration configuration, ILogger<OllamaAIService> logger, ITaskDispatcher dispatcher)
    {
        _kernel = kernel;
        _httpClient = httpClient;
        _logger = logger;
        if (configuration == null)
        {
            _advancedModelEndpoint = "http://localhost:5000/api/chat";
            _advancedModelName = "gpt-4";
            _useAdvancedModel = true;
        }
        else
        {
            _advancedModelEndpoint = configuration["AI:AdvancedModelEndpoint"] ?? "http://localhost:5000/api/chat";
            _advancedModelName = configuration["AI:AdvancedModelName"] ?? "gpt-4";
            try
            {
                _useAdvancedModel = configuration.GetValue<bool?>("AI:UseAdvancedModel") ?? true;
            }
            catch
            {
                _useAdvancedModel = true;
            }
        }
        _dispatcher = dispatcher;
        // Do not eagerly enumerate all task handlers during construction
        // (those handlers may require heavy services like DB, storage, or cache)
        // Resolve known tasks lazily during request processing if needed
        _knownTasks = new List<string>();
    }

    public async Task<(string task, string response, bool usedAdvancedModel)> ProcessRequestAsync(string input, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return ("unknown", "Please provide a request.", false);
            }

            EnsureKnownTasksLoaded();

            _logger.LogInformation("IAIService.ProcessRequestAsync entered; inputLength={L}", input.Length);

            // Classify with intent-aware prompt + normalization (reuse ClassifyOnlyAsync)
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
                        var (handled, advancedResponse) = await _dispatcher.TryHandleAsync("advanced", input);

                        // If the advanced handler returned a response, respect it (and mark it).
                        if (handled && !string.IsNullOrWhiteSpace(advancedResponse))
                        {
                            if (IsAdvancedUnavailableResponse(advancedResponse))
                            {
                                return ("advanced", BuildAdvancedUnavailableResponse(), false);
                            }

                            return ("advanced", advancedResponse, true);
                        }

                        // If we reached here, the advanced handler didn't handle the request.
                        // The system is configured to use the advanced model, so treat this
                        // as an unavailable advanced model and return an error message
                        // instead of falling back to the local model.
                        _logger.LogWarning("Advanced model handler did not handle request; returning unavailable response (no fallback)");
                        return ("advanced", BuildAdvancedUnavailableResponse(), false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Advanced model call failed; returning unavailable response (no fallback)");
                        return ("advanced", BuildAdvancedUnavailableResponse(), false);
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
                _logger.LogError(ex, "Handler for task {Task} failed, falling back to model processing", detectedTask);
            }

            // Process with AI model if no task handler is available
            try
            {
                var chatService = _kernel.GetRequiredService<IChatCompletionService>();
                var processingPrompt = $@"
You are a helpful assistant processing a {detectedTask} task.

User request: {input}

Please provide a helpful and concise response:";

                _logger.LogDebug("Calling chatService.GetChatMessageContentAsync for processing prompt (detectedTask={Task})", detectedTask);
                var swProc = Stopwatch.StartNew();
                var response = await chatService.GetChatMessageContentAsync(processingPrompt, cancellationToken: cancellationToken);
                swProc.Stop();
                _logger.LogInformation("Local model processing completed (detectedTask={Task}) in {Ms}ms, responseLen={Len}", detectedTask, swProc.ElapsedMilliseconds, response.Content?.Length ?? 0);
                return (detectedTask, response.Content ?? "I couldn't process that request.", false);
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
        ["document-search"]    = "user wants to search through their documents",
        ["document-query"]     = "user wants to ask a question about the contents of a document",
        ["document-highlight"] = "user wants to highlight text or a section in a document",
        ["ocr"]                = "user wants to scan text, read text from an image, or perform OCR",
        ["ocr-retry"]          = "user wants to retry or redo an OCR scan",
        ["open-file"]          = "user wants to open, browse, pick, upload, or choose a file from their device",
        ["select-document"]    = "user wants to select, view, preview, or load a previously uploaded document",
        ["advanced"]           = "user wants advanced features, settings, or RAG document processing",
        ["template"]           = "user wants to use or fill in a template",
    };

    public async Task<string> ClassifyOnlyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        EnsureKnownTasksLoaded();

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
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();
            var result = await chatService.GetChatMessageContentAsync(prompt, cancellationToken: cancellationToken);
            var raw = result.Content?.Trim().ToLowerInvariant() ?? "unknown";
            return NormalizeDetectedTask(raw);
        }
        catch { return "unknown"; }
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

    private static bool IsAdvancedUnavailableResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return true;
        var lowered = response.ToLowerInvariant();
        return lowered.Contains("not configured")
            || lowered.Contains("error calling advanced model")
            || lowered.Contains("unavailable")
            || lowered.Contains("returned 4")
            || lowered.Contains("returned 5");
    }

    private string BuildAdvancedUnavailableResponse()
    {
        var availableTasks = _knownTasks
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(t => !string.Equals(t, "advanced", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        if (availableTasks.Count == 0)
        {
            return "Advanced tasks are unavailable right now. No local tasks are currently registered.";
        }

        var taskLines = availableTasks.Select(task =>
        {
            if (TaskSummaryMap.TryGetValue(task, out var summary) && !string.IsNullOrWhiteSpace(summary))
            {
                return $"- {task}: {summary}";
            }
            return $"- {task}: Available local task handler.";
        });

        return "Advanced tasks are unavailable right now. Available tasks:\n" + string.Join("\n", taskLines);
    }
}
