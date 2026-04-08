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

    public IReadOnlyList<string> Keywords =>
    new[]
    {
        "ask the document",
        "preguntar al documento",
        "query document",
        "consultar documento",
        "what does the document say",
        "qué dice el documento",
        "summarize document",
        "resumir documento",
        "explain in the document",
        "explicar en el documento",
        "according to the document",
        "según el documento",
        "based on the document",
        "basado en el documento"
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
