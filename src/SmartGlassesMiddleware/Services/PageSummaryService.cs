using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public interface IPageSummaryService
{
    Task<string> SummarizePageAsync(string pageText, string? documentName, int? pageNumber, CancellationToken cancellationToken = default);
}

public class BedrockPageSummaryService : IPageSummaryService
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BedrockPageSummaryService> _logger;

    public BedrockPageSummaryService(
        IAmazonBedrockRuntime bedrockRuntime,
        IConfiguration configuration,
        ILogger<BedrockPageSummaryService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = configuration["AWS:Bedrock:PageSummaryModelId"]
            ?? configuration["AWS:Bedrock:AdvancedModelId"]
            ?? configuration["AWS:Bedrock:ModelId"]
            ?? string.Empty;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> SummarizePageAsync(string pageText, string? documentName, int? pageNumber, CancellationToken cancellationToken = default)
    {
        var compactText = pageText?.Trim();
        if (string.IsNullOrWhiteSpace(compactText))
        {
            throw new ArgumentException("Page text is empty.", nameof(pageText));
        }

        var pageLabel = pageNumber.HasValue ? $"Page {pageNumber.Value}" : "Current page";
        var documentLabel = string.IsNullOrWhiteSpace(documentName) ? "Unknown document" : documentName.Trim();

        if (!string.IsNullOrWhiteSpace(_modelId))
        {
            try
            {
                var request = new ConverseRequest
                {
                    ModelId = _modelId,
                    System =
                    [
                        new SystemContentBlock
                        {
                            Text = "You summarize a single document page for a smart-glasses UI. Keep the summary concise, accurate, and grounded only in the provided page text. Prefer 3 to 5 short bullet points."
                        }
                    ],
                    Messages =
                    [
                        new Message
                        {
                            Role = ConversationRole.User,
                            Content =
                            [
                                new ContentBlock
                                {
                                    Text = $"Document: {documentLabel}\n{pageLabel}\n\nPlease summarize the following page content:\n\n{compactText}"
                                }
                            ]
                        }
                    ]
                };

                var response = await _bedrockRuntime.ConverseAsync(request, cancellationToken);
                var summary = string.Join("\n",
                    response.Output?.Message?.Content?
                        .Select(content => content.Text)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                    ?? Array.Empty<string>());

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    return summary.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to summarize page with Bedrock model {ModelId}, attempting Groq LLM fallback", _modelId);
            }
        }

        // Groq LLM Fallback
        var groqSummary = await TryGroqPageSummaryAsync(compactText, documentLabel, pageLabel, cancellationToken);
        if (!string.IsNullOrWhiteSpace(groqSummary))
        {
            return groqSummary;
        }

        throw new InvalidOperationException("Failed to summarize page using available AI models.");
    }

    private async Task<string?> TryGroqPageSummaryAsync(string compactText, string documentLabel, string pageLabel, CancellationToken cancellationToken)
    {
        var apiKey = _configuration["AI:Groq:ApiKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var candidateModels = new[]
        {
            "groq/compound-mini",
            _configuration["AI:Groq:CompletionModel"],
            "groq/compound"
        }.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!.Trim()).Distinct().ToList();

        var systemPrompt = "You summarize a single document page for a smart-glasses UI. Keep the summary concise, accurate, and grounded only in the provided page text. Return 3 to 5 short, clear bullet points.";
        var userPrompt = $"Document: {documentLabel}\n{pageLabel}\n\nPlease summarize the following page content:\n\n{compactText}";

        foreach (var model in candidateModels)
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new
                {
                    model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    }
                };

                using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                using var response = await http.PostAsync("https://api.groq.com/openai/v1/chat/completions", content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Groq page summary API error for model {Model} ({Status}): {Body}", model, response.StatusCode, errBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq page summary fallback failed for model {Model}", model);
            }
        }

        return null;
    }
}