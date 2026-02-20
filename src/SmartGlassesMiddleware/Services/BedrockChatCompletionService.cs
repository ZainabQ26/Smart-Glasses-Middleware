using System.Runtime.CompilerServices;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Semantic Kernel IChatCompletionService backed by Amazon Bedrock Converse API.
/// Supports any Bedrock model that accepts the Converse API (Claude, Titan, Llama, etc.).
/// </summary>
public class BedrockChatCompletionService : IChatCompletionService
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;
    private readonly ILogger<BedrockChatCompletionService> _logger;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public BedrockChatCompletionService(
        IAmazonBedrockRuntime bedrockRuntime,
        IConfiguration config,
        ILogger<BedrockChatCompletionService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = config["AWS:Bedrock:ModelId"] ?? "anthropic.claude-3-sonnet-20240229-v1:0";
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
            _logger.LogError(ex, "Bedrock Converse API call failed for model {ModelId}", _modelId);
            throw;
        }
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Fall back to non-streaming: get full response then yield it
        var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        foreach (var msg in results)
        {
            yield return new StreamingChatMessageContent(msg.Role, msg.Content);
        }
    }
}
