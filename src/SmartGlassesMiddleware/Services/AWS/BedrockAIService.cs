using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// AWS Bedrock implementation of AI service
/// </summary>
public class BedrockAIService : IAIService
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly ILogger<BedrockAIService> _logger;
    private readonly ITaskDispatcher _dispatcher;
    private readonly string _modelId;
    private readonly List<string> _knownTasks;

    public BedrockAIService(
        IAmazonBedrockRuntime bedrockClient,
        IConfiguration configuration,
        ILogger<BedrockAIService> logger,
        ITaskDispatcher dispatcher)
    {
        _bedrockClient = bedrockClient;
        _logger = logger;
        _dispatcher = dispatcher;
        
        // Model ID with version date (20240229). Update to newer versions as they become available.
        // Check AWS Bedrock documentation for latest model versions: https://docs.aws.amazon.com/bedrock/latest/userguide/models-supported.html
        _modelId = configuration["AWS:Bedrock:ModelId"] ?? "anthropic.claude-3-sonnet-20240229-v1:0";
        _knownTasks = configuration.GetSection("AI:KnownTasks").Get<List<string>>() ?? new List<string>
        {
            "time", "reminder", "edit-reminder", "delete-reminder", "template"
        };
    }

    public async Task<(string task, string response, bool usedAdvancedModel)> ProcessRequestAsync(
        string input, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing request with Bedrock: {Input}", input);

            // Classify with intent-aware prompt + normalization (reuse ClassifyOnlyAsync)
            var task = await ClassifyOnlyAsync(input, cancellationToken);
            
            // If task is a known handler, dispatch to it
            if (task != "unknown" && task != "general")
            {
                _logger.LogInformation("Dispatching known task: {Task}", task);
                var (handled, handlerResponse) = await _dispatcher.TryHandleAsync(task, input);
                if (handled && !string.IsNullOrEmpty(handlerResponse))
                {
                    return (task, handlerResponse, true);
                }
            }

            // Generate response using Bedrock
            var response = await GenerateResponseAsync(input, cancellationToken);
            return (task, response ?? "", true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request with Bedrock");
            throw;
        }
    }

    public async Task<string> ClassifyOnlyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        var raw = await ClassifyTaskAsync(input, cancellationToken);
        // Exact match first
        if (_knownTasks.Contains(raw)) return raw;
        // Normalize: 'select document' -> 'select-document'
        var normalized = raw.Replace(" ", "-");
        if (_knownTasks.Contains(normalized)) return normalized;
        // Partial match: check if any known task starts with or equals
        var partial = _knownTasks.FirstOrDefault(t => t == normalized || normalized.StartsWith(t) || t.StartsWith(normalized));
        return partial ?? "unknown";
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

    private async Task<string> ClassifyTaskAsync(string input, CancellationToken cancellationToken)
    {
        var taskLines = _knownTasks
            .Select(t => TaskDescriptions.TryGetValue(t, out var desc)
                ? $"- {t}: {desc}"
                : $"- {t}")
            .ToList();

        var prompt = $"""
            You are a voice command classifier for smart glasses. Match the user's request to the most fitting task by INTENT, not exact wording.

            Available tasks:
            {string.Join("\n", taskLines)}

            Rules:
            - Respond with ONLY the task name (e.g. open-file). No explanation.
            - If none match, respond with "general".
            - Interpret variations: "can you open a file" → open-file, "what's the time" → time, "pick a document" → select-document, etc.

            User request: {input}
            Task:
            """;

        try
        {
            var classification = await InvokeClaudeAsync(prompt, maxTokens: 50, cancellationToken: cancellationToken);
            return classification.Trim().ToLower();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying task");
            return "general";
        }
    }

    private async Task<string> GenerateResponseAsync(string input, CancellationToken cancellationToken)
    {
        var prompt = $"""
            You are a helpful AI assistant for smart glasses.
            Provide a concise, helpful response to the user's request.
            
            User request: {input}
            """;

        return await InvokeClaudeAsync(prompt, maxTokens: 1000, cancellationToken: cancellationToken);
    }

    public async Task<bool> WillUseAdvancedModelAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        try
        {
            var detected = await ClassifyOnlyAsync(input, cancellationToken);
            return string.Equals(detected, "unknown", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> InvokeClaudeAsync(
        string prompt, 
        int maxTokens = 1000, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Prepare request for Claude
            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = maxTokens,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                temperature = 0.7,
                top_p = 0.9
            };

            var jsonRequest = JsonSerializer.Serialize(requestBody);
            
            var request = new InvokeModelRequest
            {
                ModelId = _modelId,
                Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonRequest)),
                ContentType = "application/json",
                Accept = "application/json"
            };

            var response = await _bedrockClient.InvokeModelAsync(request, cancellationToken);

            // Parse response
            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseBody);

            // Extract text from Claude response format
            var content = jsonResponse.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();

            return content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking Bedrock Claude model");
            throw;
        }
    }
}
