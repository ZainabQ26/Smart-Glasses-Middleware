using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

#pragma warning disable SKEXP0001

namespace SmartGlassesMiddleware.Services;

public interface IRAGService
{
    Task<string> QueryDocumentsAsync(string query, int topK = 5, List<Guid>? groupIds = null);
    Task<List<DocumentChunk>> SearchChunksAsync(string query, int topK = 5, List<Guid>? groupIds = null);
    Task GenerateEmbeddingsAsync(Guid documentId);
    Task<int> ReprocessFailedDocumentsAsync();
    Task<List<DocumentMetadata>> GetUnprocessedDocumentsAsync();
}

public class RAGService : IRAGService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RAGService> _logger;
    private readonly IAIService _aiService;
    private readonly Kernel _kernel;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;

    public RAGService(
        ApplicationDbContext context,
        ILogger<RAGService> logger,
        IAIService aiService,
        Kernel kernel,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _aiService = aiService;
        _kernel = kernel;
        _maxRetries = configuration.GetValue<int?>("RAG:MaxRetries") ?? 3;
        _retryDelayMs = configuration.GetValue<int?>("RAG:RetryDelayMs") ?? 1000;
    }

    public async Task<string> QueryDocumentsAsync(string query, int topK = 5, List<Guid>? groupIds = null)
    {
        try
        {
            // Search for relevant chunks
            var relevantChunks = await SearchChunksAsync(query, topK, groupIds);

            if (!relevantChunks.Any())
            {
                return "No relevant information found in the uploaded documents.";
            }

            // Build context from chunks
            var context = await BuildContextFromChunksAsync(relevantChunks, groupIds);

            // Generate response using AI with context
            var prompt = $@"Based on the following context from documents, answer the question. 
If you reference specific information, mention which page it came from.

Context:
{context}

Question: {query}

Answer:";

            var (_, response, _) = await _aiService.ProcessRequestAsync(prompt);
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents with query: {Query}", query);
            throw;
        }
    }

    public async Task<List<DocumentChunk>> SearchChunksAsync(string query, int topK = 5, List<Guid>? groupIds = null)
    {
        try
        {
            // Get query embedding
            var queryEmbedding = await GenerateQueryEmbeddingAsync(query);

            // Build query for chunks, optionally filtered by groups
            var chunksQuery = _context.DocumentChunks
                .Include(c => c.DocumentMetadata)
                .ThenInclude(d => d.DocumentGroups)
                .Where(c => c.Embedding != null);

            // Filter by groups if specified
            if (groupIds != null && groupIds.Any())
            {
                chunksQuery = chunksQuery.Where(c => 
                    c.DocumentMetadata.DocumentGroups.Any(dg => groupIds.Contains(dg.ResourceGroupId)));
            }

            var allChunks = await chunksQuery.ToListAsync();

            if (!allChunks.Any())
            {
                _logger.LogWarning("No chunks with embeddings found for query: {Query}", query);
                return new List<DocumentChunk>();
            }

            // Calculate similarity scores
            var scoredChunks = allChunks.Select(chunk => new
            {
                Chunk = chunk,
                Score = CalculateCosineSimilarity(queryEmbedding, DeserializeEmbedding(chunk.Embedding!))
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

            return scoredChunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching chunks");
            
            // Fallback to keyword search
            return await KeywordSearchAsync(query, topK, groupIds);
        }
    }

    public async Task GenerateEmbeddingsAsync(Guid documentId)
    {
        var chunks = await _context.DocumentChunks
            .Where(c => c.DocumentMetadataId == documentId)
            .ToListAsync();

        var failedChunks = 0;
        foreach (var chunk in chunks)
        {
            var success = await GenerateEmbeddingWithRetryAsync(chunk);
            if (!success)
            {
                failedChunks++;
            }
        }

        await _context.SaveChangesAsync();

        if (failedChunks > 0)
        {
            _logger.LogWarning("Failed to generate embeddings for {FailedCount} out of {TotalCount} chunks in document {DocumentId}", 
                failedChunks, chunks.Count, documentId);
        }
    }

    public async Task<int> ReprocessFailedDocumentsAsync()
    {
        var unprocessedDocs = await GetUnprocessedDocumentsAsync();
        var reprocessedCount = 0;

        foreach (var doc in unprocessedDocs)
        {
            try
            {
                _logger.LogInformation("Reprocessing document {DocumentId}: {FileName}", doc.Id, doc.FileName);
                await GenerateEmbeddingsAsync(doc.Id);
                
                doc.IsProcessed = true;
                doc.ProcessedAt = DateTime.UtcNow;
                doc.ProcessingError = null;
                
                reprocessedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reprocess document {DocumentId}", doc.Id);
                doc.ProcessingError = ex.Message;
            }
        }

        await _context.SaveChangesAsync();
        return reprocessedCount;
    }

    public async Task<List<DocumentMetadata>> GetUnprocessedDocumentsAsync()
    {
        return await _context.DocumentMetadatas
            .Where(d => !d.IsProcessed || d.ProcessingError != null)
            .OrderBy(d => d.UploadedAt)
            .ToListAsync();
    }

    private async Task<bool> GenerateEmbeddingWithRetryAsync(DocumentChunk chunk)
    {
        for (int attempt = 0; attempt < _maxRetries; attempt++)
        {
            try
            {
                var embedding = await GenerateTextEmbeddingAsync(chunk.ChunkText);
                chunk.Embedding = SerializeEmbedding(embedding);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attempt {Attempt} failed to generate embedding for chunk {ChunkId}", 
                    attempt + 1, chunk.Id);
                
                if (attempt < _maxRetries - 1)
                {
                    await Task.Delay(_retryDelayMs * (attempt + 1)); // Exponential backoff
                }
            }
        }
        
        return false;
    }

    private async Task<float[]> GenerateTextEmbeddingAsync(string text)
    {
        try
        {
            // Try to use Semantic Kernel's embedding service
            var embeddingService = _kernel.Services.GetService(typeof(ITextEmbeddingGenerationService)) as ITextEmbeddingGenerationService;
            
            if (embeddingService != null)
            {
                var embedding = await embeddingService.GenerateEmbeddingAsync(text);
                return embedding.ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding service not available");
        }

        // Fallback: simple hash-based embedding (for demonstration)
        // In production, use a proper embedding model
        return GenerateSimpleEmbedding(text);
    }

    private async Task<float[]> GenerateQueryEmbeddingAsync(string query)
    {
        return await GenerateTextEmbeddingAsync(query);
    }

    private float[] GenerateSimpleEmbedding(string text)
    {
        // Simple hash-based embedding for fallback
        // In production, use proper embedding models like text-embedding-ada-002
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var embedding = new float[384]; // Common embedding dimension
        
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1); // Range: -1 to 1
        }
        
        // Normalize
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= magnitude;
        }
        
        return embedding;
    }

    private string SerializeEmbedding(float[] embedding)
    {
        return JsonSerializer.Serialize(embedding);
    }

    private float[] DeserializeEmbedding(string embeddingJson)
    {
        return JsonSerializer.Deserialize<float[]>(embeddingJson) ?? Array.Empty<float>();
    }

    private float CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0;

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = (float)Math.Sqrt(magnitudeA);
        magnitudeB = (float)Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    private async Task<List<DocumentChunk>> KeywordSearchAsync(string query, int topK, List<Guid>? groupIds = null)
    {
        var keywords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var chunksQuery = _context.DocumentChunks
            .Include(c => c.DocumentMetadata)
            .ThenInclude(d => d.DocumentGroups)
            .Where(c => keywords.Any(k => c.ChunkText.ToLower().Contains(k)));

        // Filter by groups if specified
        if (groupIds != null && groupIds.Any())
        {
            chunksQuery = chunksQuery.Where(c => 
                c.DocumentMetadata.DocumentGroups.Any(dg => groupIds.Contains(dg.ResourceGroupId)));
        }

        var chunks = await chunksQuery
            .OrderByDescending(c => keywords.Count(k => c.ChunkText.ToLower().Contains(k)))
            .Take(topK)
            .ToListAsync();

        return chunks;
    }

    private async Task<string> BuildContextFromChunksAsync(List<DocumentChunk> chunks, List<Guid>? groupIds = null)
    {
        var context = new StringBuilder();
        
        // Add context from document chunks
        foreach (var chunk in chunks)
        {
            context.AppendLine($"[From {chunk.DocumentMetadata.FileName}, Page {chunk.PageNumber}]:");
            context.AppendLine(chunk.ChunkText);
            context.AppendLine();
        }

        // If group IDs are provided, add additional context from group prompts
        if (groupIds != null && groupIds.Any())
        {
            var groupPrompts = await _context.ResourceGroups
                .Where(g => groupIds.Contains(g.Id))
                .Include(g => g.PromptGroups)
                .ThenInclude(pg => pg.ManagedPrompt)
                .SelectMany(g => g.PromptGroups.Select(pg => pg.ManagedPrompt))
                .Distinct()
                .ToListAsync();

            if (groupPrompts.Any())
            {
                context.AppendLine("--- Additional Context from Groups ---");
                foreach (var prompt in groupPrompts)
                {
                    context.AppendLine($"[{prompt.Name}]:");
                    context.AppendLine(prompt.PromptText);
                    context.AppendLine();
                }
            }
        }

        return context.ToString();
    }
}
