using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Local task handler for RAG-based document queries.
/// </summary>
public class DocumentQueryTaskHandler : ITaskHandler
{
    private readonly IAIService _aiService;
    private readonly ILogger<DocumentQueryTaskHandler> _logger;

    public string Name => "document-query";

    public IReadOnlyList<string> Keywords => new[]
    {
        "ask the document", "query document", "what does the document say", "summarize document",
        "explain in the document", "according to the document", "based on the document"
    };

    public DocumentQueryTaskHandler(IAIService aiService, ILogger<DocumentQueryTaskHandler> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            _logger.LogInformation("Processing document query: {Query}", input);

            return "I will search the document for you.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document query");
            return $"Error querying documents: {ex.Message}";
        }
    }
}
