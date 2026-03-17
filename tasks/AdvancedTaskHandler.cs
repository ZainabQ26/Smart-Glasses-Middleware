using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace SmartGlassesMiddleware.Services;

public class AdvancedTaskHandler : ITaskHandler
{
    // Centralized fallback used when advanced handling cannot run at all.
    public const string DispatcherFallbackMessage = "Advanced tasks are unavailable: failed to route request to advanced handler.";
    public const string ModelNotConfiguredMessage = "Advanced tasks are unavailable: AWS Bedrock model is not configured.";
    public const string EmptyResponseMessage = "Advanced tasks are unavailable: model returned an empty response.";
    public const string InvocationFailedMessage = "Advanced tasks are unavailable: failed to call Amazon Bedrock.";

    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;

    private readonly ILogger<AdvancedTaskHandler> _logger;

    public AdvancedTaskHandler(IAmazonBedrockRuntime bedrockRuntime, IConfiguration configuration, ILogger<AdvancedTaskHandler> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = configuration["AWS:Bedrock:AdvancedModelId"]
            ?? configuration["AWS:Bedrock:ModelId"]
            ?? string.Empty;
        _logger = logger;
    }

    public string Name => "advanced";

    public IReadOnlyList<string> Keywords => Array.Empty<string>(); // AI fallback; no direct keywords


    public async Task<string> HandleAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
        {
            _logger.LogWarning("Advanced Bedrock model not configured (AWS:Bedrock:AdvancedModelId or AWS:Bedrock:ModelId)");
            return ModelNotConfiguredMessage;
        }

        try
        {
            var request = new ConverseRequest
            {
                ModelId = _modelId,
                Messages =
                [
                    new Message
                    {
                        Role = ConversationRole.User,
                        Content = [new ContentBlock { Text = input }]
                    }
                ]
            };

            var response = await _bedrockRuntime.ConverseAsync(request);
            var text = string.Join("\n",
                response.Output?.Message?.Content?
                    .Select(c => c.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                ?? Array.Empty<string>());

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Bedrock advanced model returned empty output for model {ModelId}", _modelId);
                return EmptyResponseMessage;
            }

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Amazon Bedrock advanced model {ModelId}", _modelId);
            return InvocationFailedMessage;
        }
    }
}
