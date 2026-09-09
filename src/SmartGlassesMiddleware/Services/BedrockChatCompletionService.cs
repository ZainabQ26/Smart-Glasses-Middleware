using System.Runtime.CompilerServices;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Semantic Kernel IChatCompletionService backed by Amazon Bedrock Converse API with Groq LLM fallback.
/// </summary>
public class BedrockChatCompletionService : IChatCompletionService
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;
    private readonly IConfiguration _config;
    private readonly ILogger<BedrockChatCompletionService> _logger;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public BedrockChatCompletionService(
        IAmazonBedrockRuntime bedrockRuntime,
        IConfiguration config,
        ILogger<BedrockChatCompletionService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = config["AWS:Bedrock:ModelId"] ?? "us.amazon.nova-2-lite-v1:0";
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var messages = chatHistory
            .Where(m => m.Role != AuthorRole.System)
            .Select(m => new Message
            {
                Role = m.Role == AuthorRole.User ? ConversationRole.User : ConversationRole.Assistant,
                Content = [new ContentBlock { Text = m.Content ?? string.Empty }]
            })
            .ToList();

        var request = new ConverseRequest
        {
            ModelId = _modelId,
            Messages = messages,
        };

        var systemMsg = chatHistory.FirstOrDefault(m => m.Role == AuthorRole.System);
        if (systemMsg != null)
        {
            request.System = [new SystemContentBlock { Text = systemMsg.Content ?? string.Empty }];
        }

        // Try Groq LLM first (fastest LPU execution < 500ms)
        var groqText = await TryGroqCompletionAsync(chatHistory, cancellationToken);
        if (!string.IsNullOrWhiteSpace(groqText))
        {
            return [new ChatMessageContent(AuthorRole.Assistant, groqText)];
        }

        try
        {
            _logger.LogDebug("Invoking Bedrock Converse API with model {ModelId}, {MsgCount} messages", _modelId, messages.Count);
            var response = await _bedrockRuntime.ConverseAsync(request, cancellationToken);
            var responseText = response.Output?.Message?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            _logger.LogDebug("Bedrock Converse API returned {Len} chars, stopReason={Reason}", responseText.Length, response.StopReason);
            return [new ChatMessageContent(AuthorRole.Assistant, responseText)];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bedrock Converse API call failed for model {ModelId}", _modelId);
            throw;
        }
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        foreach (var msg in results)
        {
            yield return new StreamingChatMessageContent(msg.Role, msg.Content);
        }
    }

    private async Task<string?> TryGroqCompletionAsync(ChatHistory chatHistory, CancellationToken cancellationToken)
    {
        var apiKey = _config["AI:Groq:ApiKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var candidateModels = new[]
        {
            "groq/compound-mini",
            _config["AI:Groq:CompletionModel"],
            "groq/compound"
        }.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!.Trim()).Distinct().ToList();

        var messagesPayload = chatHistory.Select(m => new
        {
            role = m.Role == AuthorRole.System ? "system" : (m.Role == AuthorRole.User ? "user" : "assistant"),
            content = m.Content ?? string.Empty
        }).ToList();

        foreach (var model in candidateModels)
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new { model, messages = messagesPayload };
                using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                using var response = await http.PostAsync("https://api.groq.com/openai/v1/chat/completions", content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq chat completion fallback failed for model {Model}", model);
            }
        }

        return null;
    }
}
