using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Local task handler for RAG-based document queries.
/// </summary>
public class DocumentQueryTaskHandler : ITaskHandler
{
    private readonly IRAGService _ragService;
    private readonly bool _ragEnabled;
    private readonly ILogger<DocumentQueryTaskHandler> _logger;

    public string Name => "document-query";

    public IReadOnlyList<string> Keywords => new[]
    {
        "ask the document", "query document", "what does the document say", "summarize document",
        "explain in the document", "according to the document", "based on the document"
    };

    public DocumentQueryTaskHandler(IRAGService ragService, IConfiguration configuration, ILogger<DocumentQueryTaskHandler> _logger)
    {
        _ragService = ragService;
        _ragEnabled = configuration.GetValue<bool?>("RAG:Enabled") ?? true;
        this._logger = _logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            if (!_ragEnabled)
            {
                return "Document query is currently unavailable because RAG indexing is disabled.";
            }

            _logger.LogInformation("Processing document query: {Query}", input);

            // Use RAG service to query documents
            var result = await _ragService.QueryDocumentsAsync(input, groupIds: null);

            // Format the response with sources
            var response = $"📚 {result.Answer}\n\n";

            if (result.Sources != null && result.Sources.Any())
            {
                response += "**Sources:**\n";
                foreach (var source in result.Sources.Take(5)) // Show top 5 sources
                {
                    response += $"• {source.FileName} (Page {source.PageNumber})\n";
                }
            }

            // Add metadata
            if (result.UsedAdvancedModel)
            {
                response += $"\n⚡ Processed with advanced model";
            }
            else
            {
                response += $"\n🏠 Processed locally";
            }

            if (result.TokenCount > 0)
            {
                response += $" ({result.TokenCount} tokens)";
            }

            // Add referenced sources if available
            if (result.ReferencedSources != null && result.ReferencedSources.Any())
            {
                response += $"\n\n**AI Referenced:**\n";
                foreach (var source in result.ReferencedSources.Take(3))
                {
                    response += $"✓ {source.FileName} (Page {source.PageNumber})\n";
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document query");
            return $"Error querying documents: {ex.Message}";
        }
    }
}
