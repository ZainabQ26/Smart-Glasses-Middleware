using Microsoft.SemanticKernel;
using System.ComponentModel;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Semantic Kernel plugin that enables the AI to query the RAG system for information
/// </summary>
public class RAGPlugin
{
    private readonly IRAGService _ragService;
    private readonly ILogger<RAGPlugin> _logger;
    private readonly List<Guid>? _groupIds;
    private readonly List<Guid>? _documentIds;
    private readonly List<DocumentSource> _queriedSources;
    private readonly int _topK;

    public RAGPlugin(
        IRAGService ragService, 
        ILogger<RAGPlugin> logger,
        List<Guid>? groupIds = null,
        List<Guid>? documentIds = null,
        int topK = 3)
    {
        _ragService = ragService;
        _logger = logger;
        _groupIds = groupIds;
        _documentIds = documentIds;
        _queriedSources = new List<DocumentSource>();
        _topK = topK;
    }

    /// <summary>
    /// Gets the sources that were queried during the conversation
    /// </summary>
    public List<DocumentSource> QueriedSources => _queriedSources;

    [KernelFunction("search_documents")]
    [Description("Search the document database for information related to a specific question or topic. Returns relevant text snippets from documents.")]
    public async Task<string> SearchDocuments(
        [Description("The search query or question to find relevant information for")] string query)
    {
        try
        {
            _logger.LogInformation("AI requesting document search for query: {Query}", query);

            // Search for relevant chunks
            var chunks = await _ragService.SearchChunksAsync(query, _topK, _groupIds, _documentIds);

            if (!chunks.Any())
            {
                _logger.LogInformation("No relevant documents found for AI query: {Query}", query);
                return "No relevant documents found for this query.";
            }

            // Track sources for later reporting
            foreach (var chunk in chunks)
            {
                var source = new DocumentSource
                {
                    DocumentId = chunk.DocumentMetadataId,
                    FileName = chunk.DocumentMetadata.FileName,
                    PageNumber = chunk.PageNumber,
                    ChunkId = chunk.Id,
                    ChunkText = chunk.ChunkText.Length > 200 ? chunk.ChunkText.Substring(0, 200) + "..." : chunk.ChunkText
                };
                
                // Only add if not already tracked
                if (!_queriedSources.Any(s => s.ChunkId == source.ChunkId))
                {
                    _queriedSources.Add(source);
                }
            }

            // Format the results for the AI
            var result = new System.Text.StringBuilder();
            result.AppendLine("Found relevant information:");
            result.AppendLine();

            foreach (var chunk in chunks)
            {
                result.AppendLine($"[Source: {chunk.DocumentMetadata.FileName}, Page {chunk.PageNumber}]:");
                result.AppendLine(chunk.ChunkText);
                result.AppendLine();
            }

            _logger.LogInformation("Returned {Count} document chunks to AI for query: {Query}", chunks.Count, query);
            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching documents for AI query: {Query}", query);
            return "Error occurred while searching documents. Please try rephrasing your question.";
        }
    }
}
