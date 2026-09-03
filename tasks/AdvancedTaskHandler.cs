using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public class AdvancedTaskHandler : ITaskHandler
{
    // Centralized fallback used when advanced handling cannot run at all.
    public const string DispatcherFallbackMessage = "Advanced tasks are unavailable: failed to route request to advanced handler.";
    public const string ModelNotConfiguredMessage = "Advanced tasks are unavailable: AWS Bedrock model is not configured.";
    public const string EmptyResponseMessage = "Advanced tasks are unavailable: model returned an empty response.";
    public const string InvocationFailedMessage = "Advanced tasks are unavailable: failed to call Amazon Bedrock or Groq API.";

    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdvancedTaskHandler> _logger;

    public AdvancedTaskHandler(IAmazonBedrockRuntime bedrockRuntime, IConfiguration configuration, ILogger<AdvancedTaskHandler> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = configuration["AWS:Bedrock:AdvancedModelId"]
            ?? configuration["AWS:Bedrock:ModelId"]
            ?? string.Empty;
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => "advanced";

    public IReadOnlyList<string> Keywords => Array.Empty<string>(); // AI fallback; no direct keywords

    public async Task<string> HandleAsync(string input)
    {
        if (!string.IsNullOrWhiteSpace(_modelId))
        {
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

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bedrock call failed for model {ModelId}, attempting Groq LLM fallback", _modelId);
            }
        }

        // Groq Fallback when Bedrock is unavailable or unconfigured
        var groqText = await TryGroqCompletionAsync(input);
        if (!string.IsNullOrWhiteSpace(groqText))
        {
            return groqText;
        }

        return InvocationFailedMessage;
    }

    private async Task<string?> TryGroqCompletionAsync(string prompt)
    {
        var apiKey = _configuration["AI:Groq:ApiKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var candidateModels = new[]
        {
            _configuration["AI:Groq:CompletionModel"],
            "groq/compound-mini",
            "openai/gpt-oss-120b",
            "groq/compound"
        }.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!.Trim()).Distinct().ToList();

        foreach (var model in candidateModels)
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new
                {
                    model,
                    messages = new[] { new { role = "user", content = prompt } }
                };

                using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                using var response = await http.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Groq API model {Model} returned HTTP {StatusCode}: {Body}", model, response.StatusCode, errBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq completion fallback failed for model {Model}", model);
            }
        }

        return null;
    }
}
