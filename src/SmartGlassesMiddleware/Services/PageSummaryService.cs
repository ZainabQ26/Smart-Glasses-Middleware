using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace SmartGlassesMiddleware.Services;

public interface IPageSummaryService
{
    Task<string> SummarizePageAsync(string pageText, string? documentName, int? pageNumber, CancellationToken cancellationToken = default);
}

public class BedrockPageSummaryService : IPageSummaryService
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;
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
        _logger = logger;
    }

    public async Task<string> SummarizePageAsync(string pageText, string? documentName, int? pageNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_modelId))
        {
            throw new InvalidOperationException("Page summary model is not configured.");
        }

        var compactText = pageText.Trim();
        if (string.IsNullOrWhiteSpace(compactText))
        {
            throw new ArgumentException("Page text is empty.", nameof(pageText));
        }

        var pageLabel = pageNumber.HasValue ? $"Page {pageNumber.Value}" : "Current page";
        var documentLabel = string.IsNullOrWhiteSpace(documentName) ? "Unknown document" : documentName.Trim();

        var request = new ConverseRequest
        {
            ModelId = _modelId,
            System =
            [
                new SystemContentBlock
                {
                    Text = "You summarize a single document page for a smart-glasses UI. Keep the summary concise, accurate, and grounded only in the provided page text. Prefer 3 to 6 short bullet points when the content is informational. Mention if the page text appears incomplete or noisy."
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

        try
        {
            var response = await _bedrockRuntime.ConverseAsync(request, cancellationToken);
            var summary = string.Join("\n",
                response.Output?.Message?.Content?
                    .Select(content => content.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                ?? Array.Empty<string>());

            if (string.IsNullOrWhiteSpace(summary))
            {
                throw new InvalidOperationException("Bedrock returned an empty page summary.");
            }

            return summary.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize page with Bedrock model {ModelId}", _modelId);
            throw;
        }
    }
}