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

            // Classify the task
            var task = await ClassifyTaskAsync(input, cancellationToken);
            
            // If task is recognized, dispatch to handler
            if (_knownTasks.Contains(task.ToLower()))
            {
                _logger.LogInformation("Dispatching known task: {Task}", task);
                var handlerResponse = await _dispatcher.DispatchTaskAsync(task, input, cancellationToken);
                return (task, handlerResponse, true);
            }

            // Generate response using Bedrock
            var response = await GenerateResponseAsync(input, cancellationToken);
            return (task, response, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request with Bedrock");
            throw;
        }
    }

    private async Task<string> ClassifyTaskAsync(string input, CancellationToken cancellationToken)
    {
        var prompt = $"""
            Classify the following user request into one of these categories:
            {string.Join(", ", _knownTasks)}
            
            If it doesn't match any category, respond with "general".
            Respond with only the category name, nothing else.
            
            User request: {input}
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
