using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using System.Text;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Service for processing files through RAG (Retrieval-Augmented Generation) to make them searchable by AI
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Process a file and add it to the RAG index for AI retrieval
    /// </summary>
    Task ProcessFileForRagAsync(Guid fileId, string fileName, string contentType, string storageKey, string? transcription = null);
    
    /// <summary>
    /// Get context from RAG system for a given query
    /// </summary>
    Task<string> GetRagContextAsync(string query, int maxResults = 5);
}

/// <summary>
/// Implementation of RAG service that stores file metadata and content in PostgreSQL
/// for AI context retrieval
/// </summary>
public class RagService : IRagService
{
    private readonly ApplicationDbContext _context;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ILogger<RagService> _logger;

    public RagService(
        ApplicationDbContext context,
        IMediaStorageService mediaStorageService,
        ILogger<RagService> logger)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _logger = logger;
    }

    public async Task ProcessFileForRagAsync(Guid fileId, string fileName, string contentType, string storageKey, string? transcription = null)
    {
        try
        {
            _logger.LogInformation("Processing file {FileId} ({FileName}) for RAG indexing", fileId, fileName);

            // Create a RAG entry in the database
            var ragEntry = new RagEntry
            {
                Id = Guid.NewGuid(),
                FileId = fileId,
                FileName = fileName,
                ContentType = contentType,
                StorageKey = storageKey,
                Transcription = transcription,
                ProcessedAt = DateTime.UtcNow,
                IndexedContent = BuildIndexedContent(fileName, contentType, transcription)
            };

            _context.RagEntries.Add(ragEntry);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully indexed file {FileId} for RAG", fileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file {FileId} for RAG", fileId);
            throw;
        }
    }

    public async Task<string> GetRagContextAsync(string query, int maxResults = 5)
    {
        try
        {
            _logger.LogInformation("Retrieving RAG context for query: {Query}", query);

            // Simple keyword-based search (in a production system, use vector embeddings)
            var keywords = ExtractKeywords(query);
            
            var entries = await _context.RagEntries
                .Where(e => keywords.Any(k => 
                    EF.Functions.ILike(e.IndexedContent, $"%{k}%") || 
                    EF.Functions.ILike(e.FileName, $"%{k}%")))
                .OrderByDescending(e => e.ProcessedAt)
                .Take(maxResults)
                .ToListAsync();

            if (!entries.Any())
            {
                _logger.LogInformation("No RAG entries found for query");
                return string.Empty;
            }

            var context = new StringBuilder();
            context.AppendLine("=== Relevant Files and Context ===");
            
            foreach (var entry in entries)
            {
                context.AppendLine($"\nFile: {entry.FileName}");
                context.AppendLine($"Type: {entry.ContentType}");
                context.AppendLine($"Storage Key: {entry.StorageKey}");
                
                if (!string.IsNullOrWhiteSpace(entry.Transcription))
                {
                    context.AppendLine($"Transcription: {entry.Transcription}");
                }
                
                context.AppendLine($"Indexed: {entry.ProcessedAt:yyyy-MM-dd HH:mm:ss}");
                context.AppendLine("---");
            }

            _logger.LogInformation("Retrieved {Count} RAG entries for context", entries.Count);
            return context.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve RAG context");
            return string.Empty;
        }
    }

    private string BuildIndexedContent(string fileName, string contentType, string? transcription)
    {
        var content = new StringBuilder();
        content.AppendLine($"FileName: {fileName}");
        content.AppendLine($"ContentType: {contentType}");
        
        // Extract file extension
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension))
        {
            content.AppendLine($"FileExtension: {extension}");
        }

        // Add transcription if available
        if (!string.IsNullOrWhiteSpace(transcription))
        {
            content.AppendLine($"Transcription: {transcription}");
        }

        // Add type-specific metadata
        if (contentType.StartsWith("image/"))
        {
            content.AppendLine("MediaType: Image");
        }
        else if (contentType.StartsWith("video/"))
        {
            content.AppendLine("MediaType: Video");
        }
        else if (contentType.StartsWith("audio/"))
        {
            content.AppendLine("MediaType: Audio");
        }

        return content.ToString();
    }

    private List<string> ExtractKeywords(string query)
    {
        // Simple keyword extraction (lowercase, split by space, remove short words)
        return query
            .ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Distinct()
            .ToList();
    }
}
