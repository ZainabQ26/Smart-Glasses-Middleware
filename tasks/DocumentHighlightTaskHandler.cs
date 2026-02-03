using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Local task handler for highlighting specific text in documents and returning the highlighted copy.
/// Uses Ollama for local processing.
/// </summary>
public class DocumentHighlightTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IDocumentHighlightService _highlightService;
    private readonly ILogger<DocumentHighlightTaskHandler> _logger;

    public string Name => "document-highlight";

    public DocumentHighlightTaskHandler(
        ApplicationDbContext context, 
        IDocumentHighlightService highlightService,
        ILogger<DocumentHighlightTaskHandler> logger)
    {
        _context = context;
        _highlightService = highlightService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            // Parse input to extract document ID and text to highlight
            // Expected format: "highlight [text] in document [id]"
            // Or: "document [id] highlight [text]"
            
            Guid documentId = Guid.Empty;
            string highlightText = input.Trim();
            
            // Try to extract document ID and highlight text
            var lowerInput = input.ToLower();
            if (lowerInput.Contains("document "))
            {
                var parts = input.Split(new[] { "document ", "highlight " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                
                foreach (var part in parts)
                {
                    if (Guid.TryParse(part.Trim(), out var parsedId))
                    {
                        documentId = parsedId;
                    }
                    else if (!string.IsNullOrWhiteSpace(part) && part.ToLower() != "in")
                    {
                        highlightText = part.Trim();
                    }
                }
            }

            if (documentId == Guid.Empty)
            {
                return "Please provide a document ID. Format: 'highlight [text] in document [id]' or use the document search task first.";
            }

            _logger.LogInformation("Highlighting '{Text}' in document {DocumentId}", highlightText, documentId);

            // Get document metadata
            var document = await _context.DocumentMetadatas
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.Id == documentId);

            if (document == null)
            {
                return $"Document with ID {documentId} not found.";
            }

            // Find chunks containing the text
            var matchingChunks = document.Chunks
                .Where(c => c.ChunkText.Contains(highlightText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!matchingChunks.Any())
            {
                return $"Text '{highlightText}' not found in document '{document.FileName}'.";
            }

            // Get chunk IDs for highlighting
            var chunkIds = matchingChunks.Select(c => c.Id).ToList();

            // Generate highlighted document
            var highlightedStream = await _highlightService.HighlightDocumentAsync(
                documentId, 
                chunkIds
            );

            // In a real scenario, we'd save this to storage and return a URL
            // For now, return information about the highlighted document
            var distinctPages = matchingChunks.Select(c => c.PageNumber).Distinct().OrderBy(p => p).ToList();
            var response = $"✅ Document '{document.FileName}' highlighted successfully!\n\n";
            response += $"📄 Document: {document.FileName}\n";
            response += $"🔍 Highlighted text: '{highlightText}'\n";
            response += $"📍 Found on {matchingChunks.Count} chunk(s) across " + 
                       $"{distinctPages.Count} page(s): " + 
                       string.Join(", ", distinctPages);
            response += $"\n\n💡 Use API endpoint: GET /api/documents/{documentId}/highlight";

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error highlighting document");
            return $"Error highlighting document: {ex.Message}";
        }
    }
}
