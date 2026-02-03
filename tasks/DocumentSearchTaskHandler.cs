using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Local task handler for finding text in documents and returning location information.
/// Uses Ollama for local processing.
/// </summary>
public class DocumentSearchTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DocumentSearchTaskHandler> _logger;

    public string Name => "document-search";

    public DocumentSearchTaskHandler(ApplicationDbContext context, ILogger<DocumentSearchTaskHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            // Parse input to extract search query
            // Expected format: "find [text] in document [filename or id]"
            // Or just: "[search text]" to search all documents
            
            var searchText = input.Trim();
            string? documentFilter = null;
            
            // Simple parsing logic
            if (input.ToLower().Contains(" in document "))
            {
                var parts = input.Split(" in document ", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    searchText = parts[0].Replace("find ", "", StringComparison.OrdinalIgnoreCase).Trim();
                    documentFilter = parts[1].Trim();
                }
            }
            else if (input.ToLower().StartsWith("find "))
            {
                searchText = input.Substring(5).Trim();
            }

            _logger.LogInformation("Searching for '{SearchText}' in documents (filter: {Filter})", searchText, documentFilter);

            // Query database for matching chunks
            var query = _context.DocumentChunks
                .Include(c => c.DocumentMetadata)
                .Where(c => c.ChunkText.Contains(searchText));

            if (!string.IsNullOrEmpty(documentFilter))
            {
                query = query.Where(c => c.DocumentMetadata.FileName.Contains(documentFilter) || 
                                        c.DocumentMetadata.Id.ToString() == documentFilter);
            }

            var matches = await query
                .OrderBy(c => c.DocumentMetadata.FileName)
                .ThenBy(c => c.PageNumber)
                .Take(10) // Limit results
                .Select(c => new
                {
                    DocumentId = c.DocumentMetadataId,
                    FileName = c.DocumentMetadata.FileName,
                    PageNumber = c.PageNumber,
                    ChunkIndex = c.ChunkIndex,
                    Context = c.ChunkText.Length > 200 ? c.ChunkText.Substring(0, 200) + "..." : c.ChunkText,
                    X = c.X,
                    Y = c.Y
                })
                .ToListAsync();

            if (!matches.Any())
            {
                return $"No matches found for '{searchText}'" + 
                       (documentFilter != null ? $" in document '{documentFilter}'" : " in any documents");
            }

            // Format response
            var response = $"Found {matches.Count} match(es) for '{searchText}':\n\n";
            foreach (var match in matches)
            {
                response += $"📄 **{match.FileName}** (Page {match.PageNumber})\n";
                response += $"   Location: Chunk {match.ChunkIndex}";
                if (match.X.HasValue && match.Y.HasValue)
                {
                    response += $" (Pos: X={match.X:F2}, Y={match.Y:F2})";
                }
                response += $"\n   Context: {match.Context}\n\n";
            }

            // Add document IDs for highlighting
            response += "\n💡 To see highlighted document, use document ID: " + 
                       string.Join(", ", matches.Select(m => m.DocumentId).Distinct());

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching documents");
            return $"Error searching documents: {ex.Message}";
        }
    }
}
