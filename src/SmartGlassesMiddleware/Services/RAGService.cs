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
    Task<QueryResult> QueryDocumentsAsync(string query, int topK = 5, List<Guid>? groupIds = null);
    Task<QueryResult> QueryDocumentsWithConversationAsync(string query, Guid conversationId, int topK = 5, List<Guid>? groupIds = null);
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
    private readonly int _maxContextTokens;
    private readonly double _tokensPerChar;

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
        _maxContextTokens = configuration.GetValue<int?>("RAG:MaxContextTokens") ?? 4000;
        _tokensPerChar = configuration.GetValue<double?>("RAG:ApproximateTokensPerChar") ?? 0.25;
    }

    public async Task<QueryResult> QueryDocumentsAsync(string query, int topK = 5, List<Guid>? groupIds = null)
    {
        try
        {
            // Search for relevant chunks
            var relevantChunks = await SearchChunksAsync(query, topK, groupIds);

            if (!relevantChunks.Any())
            {
                return new QueryResult
                {
                    Answer = "No relevant information found in the uploaded documents.",
                    Sources = new List<DocumentSource>()
                };
            }

            // Build context from chunks (with token budgeting)
            var context = await BuildContextFromChunksAsync(relevantChunks, groupIds);

            // Generate response using AI with context
            var prompt = $@"Based on the following context from documents, answer the question. 
If you reference specific information, mention which page it came from.

Context:
{context}

Question: {query}

Answer:";

            var (_, response, _) = await _aiService.ProcessRequestAsync(prompt);
            
            // Build sources list
            var sources = relevantChunks.Select(c => new DocumentSource
            {
                DocumentId = c.DocumentMetadataId,
                FileName = c.DocumentMetadata.FileName,
                PageNumber = c.PageNumber,
                ChunkId = c.Id,
                ChunkText = c.ChunkText.Length > 200 ? c.ChunkText.Substring(0, 200) + "..." : c.ChunkText
            }).ToList();
            
            return new QueryResult
            {
                Answer = response,
                Sources = sources
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents with query: {Query}", query);
            throw;
        }
    }

    public async Task<QueryResult> QueryDocumentsWithConversationAsync(
        string query, 
        Guid conversationId, 
        int topK = 5, 
        List<Guid>? groupIds = null)
    {
        try
        {
            // Get recent conversation messages
            var recentMessages = await _context.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Timestamp)
                .Take(5) // Last 5 messages for context
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            // Search for relevant chunks
            var relevantChunks = await SearchChunksAsync(query, topK, groupIds);

            // Build context from chunks (with token budgeting)
            var contextBuilder = new StringBuilder();
            
            // Add conversation history first (if it fits in budget)
            if (recentMessages.Any())
            {
                contextBuilder.AppendLine("--- Recent Conversation ---");
                int historyTokens = 0;
                foreach (var msg in recentMessages)
                {
                    var msgText = $"{msg.Role}: {msg.Content}";
                    var msgTokens = EstimateTokens(msgText);
                    
                    if (historyTokens + msgTokens > _maxContextTokens / 4) // Reserve 25% for history
                        break;
                        
                    contextBuilder.AppendLine(msgText);
                    historyTokens += msgTokens;
                }
                contextBuilder.AppendLine();
            }

            // Add document context (with remaining budget)
            var usedTokens = EstimateTokens(contextBuilder.ToString());
            var remainingBudget = _maxContextTokens - usedTokens;
            
            if (remainingBudget > 500 && relevantChunks.Any())
            {
                contextBuilder.AppendLine("--- Document Context ---");
                
                foreach (var chunk in relevantChunks)
                {
                    var chunkHeader = $"[From {chunk.DocumentMetadata.FileName}, Page {chunk.PageNumber}]:";
                    var chunkText = chunk.ChunkText;
                    var chunkTokens = EstimateTokens(chunkHeader + chunkText);
                    
                    if (usedTokens + chunkTokens > _maxContextTokens)
                        break;
                        
                    contextBuilder.AppendLine(chunkHeader);
                    contextBuilder.AppendLine(chunkText);
                    contextBuilder.AppendLine();
                    usedTokens += chunkTokens;
                }
            }

            // Generate response with full context
            var prompt = $@"Based on the conversation history and document context, answer the question.

{contextBuilder}

Current Question: {query}

Answer:";

            var (_, response, _) = await _aiService.ProcessRequestAsync(prompt);
            
            // Build sources list
            var sources = relevantChunks.Select(c => new DocumentSource
            {
                DocumentId = c.DocumentMetadataId,
                FileName = c.DocumentMetadata.FileName,
                PageNumber = c.PageNumber,
                ChunkId = c.Id,
                ChunkText = c.ChunkText.Length > 200 ? c.ChunkText.Substring(0, 200) + "..." : c.ChunkText
            }).ToList();
            
            return new QueryResult
            {
                Answer = response,
                Sources = sources
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents with conversation context");
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
        var document = await _context.DocumentMetadatas
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);
            
        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found for embedding generation", documentId);
            return;
        }

        var chunks = document.Chunks.ToList();
        var failedChunks = 0;
        
        foreach (var chunk in chunks)
        {
            var success = await GenerateEmbeddingWithRetryAsync(chunk);
            if (!success)
            {
                failedChunks++;
            }
        }

        // Update document processing state based on embedding success
        if (failedChunks == 0)
        {
            document.IsProcessed = true;
            document.ProcessedAt = DateTime.UtcNow;
            document.ProcessingError = null;
        }
        else if (failedChunks < chunks.Count)
        {
            // Partial success
            document.IsProcessed = true;
            document.ProcessedAt = DateTime.UtcNow;
            document.ProcessingError = $"Failed to generate embeddings for {failedChunks} out of {chunks.Count} chunks";
            _logger.LogWarning("Partial embedding failure for document {DocumentId}: {Error}", documentId, document.ProcessingError);
        }
        else
        {
            // Complete failure
            document.IsProcessed = false;
            document.ProcessingError = $"Failed to generate embeddings for all {chunks.Count} chunks";
            _logger.LogError("Complete embedding failure for document {DocumentId}: {Error}", documentId, document.ProcessingError);
        }

        await _context.SaveChangesAsync();
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
                chunk.EmbeddingDimension = embedding.Length;
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
        // SHA256-based deterministic embedding for fallback
        // This provides consistent embeddings for the same text
        // In production, use proper embedding models like text-embedding-ada-002
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var textBytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hashBytes = sha256.ComputeHash(textBytes);
        
        const int embeddingDim = 384; // Common embedding dimension
        var embedding = new float[embeddingDim];
        
        // Use hash bytes to seed multiple rounds of hashing for full dimension
        var currentSeed = hashBytes;
        for (int i = 0; i < embeddingDim; i += 32)
        {
            // Generate more bytes by hashing the previous hash
            currentSeed = sha256.ComputeHash(currentSeed);
            
            // Convert bytes to floats
            for (int j = 0; j < 32 && (i + j) < embeddingDim; j++)
            {
                // Map byte (0-255) to float (-1 to 1)
                embedding[i + j] = (currentSeed[j] / 127.5f) - 1.0f;
            }
        }
        
        // Normalize the vector
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
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
        // Validate dimensions match
        if (a.Length == 0 || b.Length == 0)
        {
            _logger.LogWarning("Empty embedding vector encountered");
            return 0;
        }
        
        if (a.Length != b.Length)
        {
            _logger.LogWarning("Embedding dimension mismatch: {DimA} vs {DimB}", a.Length, b.Length);
            return 0;
        }

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
        {
            _logger.LogWarning("Zero magnitude embedding encountered");
            return 0;
        }

        return dotProduct / (magnitudeA * magnitudeB);
    }

    private async Task<List<DocumentChunk>> KeywordSearchAsync(string query, int topK, List<Guid>? groupIds = null)
    {
        var keywords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        
        // Build the query incrementally to ensure EF Core can translate it
        var chunksQuery = _context.DocumentChunks
            .Include(c => c.DocumentMetadata)
            .ThenInclude(d => d.DocumentGroups)
            .AsQueryable();

        // Apply keyword filtering using OR conditions (EF-translatable)
        if (keywords.Any())
        {
            IQueryable<DocumentChunk>? keywordQuery = null;
            
            foreach (var keyword in keywords)
            {
                var localKeyword = keyword; // Capture for closure
                var currentQuery = _context.DocumentChunks
                    .Where(c => EF.Functions.Like(c.ChunkText.ToLower(), $"%{localKeyword}%"));
                
                keywordQuery = keywordQuery == null ? currentQuery : keywordQuery.Union(currentQuery);
            }
            
            if (keywordQuery != null)
            {
                chunksQuery = chunksQuery.Where(c => keywordQuery.Any(kq => kq.Id == c.Id));
            }
        }

        // Filter by groups if specified
        if (groupIds != null && groupIds.Any())
        {
            chunksQuery = chunksQuery.Where(c => 
                c.DocumentMetadata.DocumentGroups.Any(dg => groupIds.Contains(dg.ResourceGroupId)));
        }

        var chunks = await chunksQuery
            .Take(topK * 3) // Get more candidates for scoring
            .ToListAsync();

        // Score and rank in memory
        var scoredChunks = chunks
            .Select(c => new
            {
                Chunk = c,
                Score = keywords.Count(k => c.ChunkText.ToLower().Contains(k))
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

        return scoredChunks;
    }

    private async Task<string> BuildContextFromChunksAsync(List<DocumentChunk> chunks, List<Guid>? groupIds = null)
    {
        var context = new StringBuilder();
        int currentTokens = 0;
        int chunksIncluded = 0;
        
        // Add context from document chunks with token budgeting
        foreach (var chunk in chunks)
        {
            var chunkHeader = $"[From {chunk.DocumentMetadata.FileName}, Page {chunk.PageNumber}]:";
            var chunkText = chunk.ChunkText;
            var chunkTokens = EstimateTokens(chunkHeader + chunkText);
            
            // Check if adding this chunk would exceed budget
            if (currentTokens + chunkTokens > _maxContextTokens)
            {
                // Try to truncate the chunk to fit
                var remainingTokens = _maxContextTokens - currentTokens - EstimateTokens(chunkHeader);
                if (remainingTokens > 100) // Only include if we can fit at least 100 tokens
                {
                    var maxChars = (int)(remainingTokens / _tokensPerChar);
                    chunkText = chunkText.Substring(0, Math.Min(maxChars, chunkText.Length)) + "... [truncated]";
                    context.AppendLine(chunkHeader);
                    context.AppendLine(chunkText);
                    context.AppendLine();
                    chunksIncluded++;
                }
                break; // Stop adding more chunks
            }
            
            context.AppendLine(chunkHeader);
            context.AppendLine(chunkText);
            context.AppendLine();
            currentTokens += chunkTokens;
            chunksIncluded++;
        }
        
        if (chunksIncluded < chunks.Count)
        {
            _logger.LogInformation("Token budget: included {Included} of {Total} chunks ({Tokens} tokens used of {Max})", 
                chunksIncluded, chunks.Count, currentTokens, _maxContextTokens);
        }

        // If group IDs are provided, add additional context from group prompts (if budget allows)
        if (groupIds != null && groupIds.Any() && currentTokens < _maxContextTokens)
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
                var groupContextHeader = "--- Additional Context from Groups ---";
                var headerTokens = EstimateTokens(groupContextHeader);
                
                if (currentTokens + headerTokens < _maxContextTokens)
                {
                    context.AppendLine(groupContextHeader);
                    currentTokens += headerTokens;
                    
                    foreach (var prompt in groupPrompts)
                    {
                        var promptHeader = $"[{prompt.Name}]:";
                        var promptTokens = EstimateTokens(promptHeader + prompt.PromptText);
                        
                        if (currentTokens + promptTokens > _maxContextTokens)
                            break;
                            
                        context.AppendLine(promptHeader);
                        context.AppendLine(prompt.PromptText);
                        context.AppendLine();
                        currentTokens += promptTokens;
                    }
                }
            }
        }

        return context.ToString();
    }
    
    private int EstimateTokens(string text)
    {
        // Rough approximation: ~4 characters per token for English text
        // This is configurable via ApproximateTokensPerChar
        return (int)(text.Length * _tokensPerChar);
    }
}
