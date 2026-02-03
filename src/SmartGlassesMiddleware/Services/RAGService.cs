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
    Task<string> QueryDocumentsAsync(string query, int topK = 5);
    Task<List<DocumentChunk>> SearchChunksAsync(string query, int topK = 5);
    Task GenerateEmbeddingsAsync(Guid documentId);
}

public class RAGService : IRAGService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RAGService> _logger;
    private readonly IAIService _aiService;
    private readonly Kernel _kernel;

    public RAGService(
        ApplicationDbContext context,
        ILogger<RAGService> logger,
        IAIService aiService,
        Kernel kernel)
    {
        _context = context;
        _logger = logger;
        _aiService = aiService;
        _kernel = kernel;
    }

    public async Task<string> QueryDocumentsAsync(string query, int topK = 5)
    {
        try
        {
            // Search for relevant chunks
            var relevantChunks = await SearchChunksAsync(query, topK);

            if (!relevantChunks.Any())
            {
                return "No relevant information found in the uploaded documents.";
            }

            // Build context from chunks
            var context = BuildContextFromChunks(relevantChunks);

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

    public async Task<List<DocumentChunk>> SearchChunksAsync(string query, int topK = 5)
    {
        try
        {
            // Get query embedding
            var queryEmbedding = await GenerateQueryEmbeddingAsync(query);

            // Get all chunks with embeddings
            var allChunks = await _context.DocumentChunks
                .Include(c => c.DocumentMetadata)
                .Where(c => c.Embedding != null)
                .ToListAsync();

            if (!allChunks.Any())
            {
                _logger.LogWarning("No chunks with embeddings found");
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
            return await KeywordSearchAsync(query, topK);
        }
    }

    public async Task GenerateEmbeddingsAsync(Guid documentId)
    {
        var chunks = await _context.DocumentChunks
            .Where(c => c.DocumentMetadataId == documentId)
            .ToListAsync();

        foreach (var chunk in chunks)
        {
            try
            {
                var embedding = await GenerateTextEmbeddingAsync(chunk.ChunkText);
                chunk.Embedding = SerializeEmbedding(embedding);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for chunk {ChunkId}", chunk.Id);
                // Continue with other chunks
            }
        }

        await _context.SaveChangesAsync();
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

    private async Task<List<DocumentChunk>> KeywordSearchAsync(string query, int topK)
    {
        var keywords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var chunks = await _context.DocumentChunks
            .Include(c => c.DocumentMetadata)
            .Where(c => keywords.Any(k => c.ChunkText.ToLower().Contains(k)))
            .OrderByDescending(c => keywords.Count(k => c.ChunkText.ToLower().Contains(k)))
            .Take(topK)
            .ToListAsync();

        return chunks;
    }

    private string BuildContextFromChunks(List<DocumentChunk> chunks)
    {
        var context = new StringBuilder();
        
        foreach (var chunk in chunks)
        {
            context.AppendLine($"[From {chunk.DocumentMetadata.FileName}, Page {chunk.PageNumber}]:");
            context.AppendLine(chunk.ChunkText);
            context.AppendLine();
        }

        return context.ToString();
    }
}
