using System.Runtime.CompilerServices;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace SmartGlassesMiddleware.Services;

public class BedrockChatCompletionService
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;
    private readonly ILogger<BedrockChatCompletionService> _logger;

    public BedrockChatCompletionService(
        IAmazonBedrockRuntime bedrockRuntime,
        IConfiguration config,
        ILogger<BedrockChatCompletionService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = config["AWS:Bedrock:ModelId"] ?? "us.amazon.nova-2-lite-v1:0";
        _logger = logger;
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var messages = new List<Message>
        {
            new Message
            {
                Role = ConversationRole.User,
                Content = [new ContentBlock { Text = userPrompt }]
            }
        };

        var request = new ConverseRequest
        {
            ModelId = _modelId,
            Messages = messages,
            System = [new SystemContentBlock { Text = systemPrompt }]
        };

        try
        {
            _logger.LogDebug("Invoking Bedrock Converse API with model {ModelId}", _modelId);
            var response = await _bedrockRuntime.ConverseAsync(request, cancellationToken);
            var responseText = response.Output?.Message?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            _logger.LogDebug("Bedrock Converse API returned {Len} chars, stopReason={Reason}", responseText.Length, response.StopReason);
            return responseText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock Converse API call failed for model {ModelId}", _modelId);
            throw;
        }
    }
}
