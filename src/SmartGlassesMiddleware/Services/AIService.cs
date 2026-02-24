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
}

public class OllamaAIService : IAIService
{
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

            if (_knownTasks.Count == 0)
            {
                try
                {
                    _knownTasks.AddRange(_dispatcher.KnownTasks);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to load known tasks lazily");
                }
            }

            _logger.LogInformation("IAIService.ProcessRequestAsync entered; inputLength={L}", input.Length);
            // Use AI model to detect the task
            var taskDetectionPrompt = $@"
You are a task classifier. Analyze the following input and determine if it matches one of these known tasks: 
{string.Join(", ", _knownTasks)}.

If it matches a known task, respond with just the task name. 
If it doesn't match any known task, respond with 'UNKNOWN'.

Input: {input}

Task:";

            string detectedTask;
            try
            {
                var chatService = _kernel.GetRequiredService<IChatCompletionService>();
                _logger.LogInformation("Invoking chatService.GetChatMessageContentAsync for task detection (info)");
                var sw = Stopwatch.StartNew();
                var taskResult = await chatService.GetChatMessageContentAsync(taskDetectionPrompt, cancellationToken: cancellationToken);
                sw.Stop();
                detectedTask = taskResult.Content?.Trim().ToLower() ?? "unknown";
                _logger.LogInformation("Detected task: {Task} for input: {Input} (taskDetectionMs={Ms}, responseLen={Len})", detectedTask, input, sw.ElapsedMilliseconds, taskResult.Content?.Length ?? 0);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Task detection was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect task using local model, defaulting to unknown");
                detectedTask = "unknown";
            }

            // If there are configured known tasks, unknown detections should go to the advanced model
            // When no known tasks are configured (common in dev), prefer local model processing.
            if (_knownTasks.Count > 0 && (detectedTask == "unknown" || !_knownTasks.Contains(detectedTask)))
            {
                if (_useAdvancedModel)
                {
                    _logger.LogInformation("Task is unknown (with known tasks configured), forwarding to advanced model");
                    try
                    {
                        var advancedResponse = await CallAdvancedModelAsync(input);
                        return ("unknown", advancedResponse, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Advanced model failed, falling back to local model");
                        // Fall through to use local model as fallback
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

    public async Task<string> ClassifyOnlyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        if (_knownTasks.Count == 0)
        {
            try { _knownTasks.AddRange(_dispatcher.KnownTasks); } catch { }
        }
        var prompt = $@"You are a task classifier. Analyze the following input and determine if it matches one of these known tasks:
{string.Join(", ", _knownTasks)}.
If it matches a known task, respond with just the task name.
If it doesn't match any known task, respond with 'UNKNOWN'.
Input: {input}
Task:";
        try
        {
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();
            var result = await chatService.GetChatMessageContentAsync(prompt, cancellationToken: cancellationToken);
            var taskName = result.Content?.Trim().ToLower() ?? "unknown";
            return _knownTasks.Contains(taskName) ? taskName : "unknown";
        }
        catch { return "unknown"; }
    }

    private async Task<string> CallAdvancedModelAsync(string input)
    {
        /*
         Temporary advanced-model stub (commented out).
         Uncomment or implement a real advanced-model HTTP call here
         when the advanced endpoint is available.

        try
        {
            _logger.LogInformation("Advanced model stub invoked for input length {L}", input?.Length ?? 0);
            await Task.Delay(10); // small async yield to keep behavior async
            return "[Advanced model placeholder response] Using advanced model (temporary stub).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Advanced model stub failed");
            return "I'm having trouble connecting to the advanced model. Please try again later.";
        }
        */

        _logger.LogInformation("Advanced model call is currently disabled (stub commented out).");
        return "Advanced model is currently disabled. Please enable the advanced endpoint to use it.";
    }
}
