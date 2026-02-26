using Microsoft.SemanticKernel;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.ChatCompletion;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using Pgvector;
using System.Diagnostics;

#pragma warning disable SKEXP0001

namespace SmartGlassesMiddleware.Services;

public interface IRAGService
{
    Task<QueryResult> QueryDocumentsAsync(string query, int topK = 5, List<Guid>? groupIds = null, List<Guid>? documentIds = null, string? monitoringRequestId = null);
    Task<QueryResult> QueryDocumentsWithConversationAsync(string query, Guid conversationId, int topK = 5, List<Guid>? groupIds = null, List<Guid>? documentIds = null);
    Task<List<DocumentChunk>> SearchChunksAsync(string query, int topK = 5, List<Guid>? groupIds = null, List<Guid>? documentIds = null);
    Task GenerateEmbeddingsAsync(Guid documentId);
    Task<int> ReprocessFailedDocumentsAsync();
    Task<List<DocumentMetadata>> GetUnprocessedDocumentsAsync();
    Task<(string embeddingJson, int dimension)> GenerateTextEmbeddingJsonAsync(string text);
    Task<int> ReindexAllDocumentsAsync(bool force = false);
    Task<QueryResult> QueryRemindersAsync(string query, int topK = 10);
    Task<QueryResult> QueryDocumentsAndRemindersAsync(string query, int topK = 5, List<Guid>? groupIds = null);
}

public class RAGService : IRAGService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RAGService> _logger;
    private readonly IAIService _aiService;
    private readonly Kernel _kernel;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRequestMonitoringService? _monitoringService;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;
    private readonly int _maxContextTokens;
    private readonly int _maxLocalModelTokens;
    private readonly double _tokensPerChar;
    private readonly int _embeddingBatchSize;
    private readonly int _embeddingMaxConcurrency;
    private readonly bool _useVectorDB;
    private readonly double _vectorSimilarityThreshold;
    private readonly int _queryTimeoutSeconds;
    private readonly int _conversationMessageLimit;
    private readonly int _conversationHistoryPercentage;
    private readonly int _embeddingDimension;

    public RAGService(
        ApplicationDbContext context,
        ILogger<RAGService> logger,
        IAIService aiService,
        Kernel kernel,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IRequestMonitoringService? monitoringService = null)
    {
        _context = context;
        _logger = logger;
        _aiService = aiService;
        _kernel = kernel;
        _serviceProvider = serviceProvider;
        _monitoringService = monitoringService;
        _maxRetries = configuration.GetValue<int?>("RAG:MaxRetries") ?? 3;
        _retryDelayMs = configuration.GetValue<int?>("RAG:RetryDelayMs") ?? 1000;
        _maxContextTokens = configuration.GetValue<int?>("RAG:MaxContextTokens") ?? 4000;
        _maxLocalModelTokens = configuration.GetValue<int?>("RAG:MaxLocalModelTokens") ?? 2000;
        _tokensPerChar = configuration.GetValue<double?>("RAG:ApproximateTokensPerChar") ?? 0.25;
        _embeddingBatchSize = configuration.GetValue<int?>("RAG:EmbeddingBatchSize") ?? 10;
        _embeddingMaxConcurrency = configuration.GetValue<int?>("RAG:EmbeddingMaxConcurrency") ?? 5;
        _useVectorDB = configuration.GetValue<bool?>("RAG:UseVectorDB") ?? true;
        _vectorSimilarityThreshold = configuration.GetValue<double?>("RAG:VectorSimilarityThreshold") ?? 0.7;
        _queryTimeoutSeconds = configuration.GetValue<int?>("RAG:QueryTimeoutSeconds") ?? 120;
        _conversationMessageLimit = configuration.GetValue<int?>("RAG:ConversationMessageLimit") ?? 5;
        _conversationHistoryPercentage = configuration.GetValue<int?>("RAG:ConversationHistoryPercentage") ?? 25;
        // Read the expected pgvector column dimension from config (must match the EF Core migration and Lambda)
        _embeddingDimension = configuration.GetValue<int?>("AWS:Bedrock:EmbeddingDimensions") ?? 1024;
    }

    public async Task<QueryResult> QueryDocumentsAsync(string query, int topK = 5, List<Guid>? groupIds = null, List<Guid>? documentIds = null, string? monitoringRequestId = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_queryTimeoutSeconds));
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Classify the raw query FIRST — skip RAG entirely for known task commands.

            // Step 1: Zero-cost direct match (e.g. "select document" -> "select-document")
            var directTaskNameQ = query.Trim().ToLower().Replace(" ", "-");
            var taskDispatcherQ = _serviceProvider.GetRequiredService<ITaskDispatcher>();
            if (!taskDispatcherQ.KnownTasks.Contains(directTaskNameQ))
            {
                // Step 2: AI classify on raw input
                directTaskNameQ = await _aiService.ClassifyOnlyAsync(query, cts.Token);
            }

            if (directTaskNameQ != "unknown" && !string.IsNullOrEmpty(directTaskNameQ))
            {
                var (handled, handlerResp) = await taskDispatcherQ.TryHandleAsync(directTaskNameQ, query);
                if (handled && !string.IsNullOrEmpty(handlerResp))
                {
                    return new QueryResult
                    {
                        Answer = handlerResp,
                        Sources = new List<DocumentSource>(),
                        UsedAdvancedModel = false,
                        ModelUsed = "local",
                        TokenCount = 0
                    };
                }
            }

            // Search for relevant chunks
            try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "Searching", "Searching document chunks", 20); } } catch {}
            var relevantChunks = await SearchChunksAsync(query, topK, groupIds, documentIds);

            try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "Searching", "Chunks found", 40); } } catch {}

            if (!relevantChunks.Any())
            {
                // No documents found, but still process the query with AI
                _logger.LogInformation("No relevant documents found for query: {Query}. Processing with AI service.", query);
                try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "AI", "No documents found, processing with AI", 50); } } catch {}
                
                // Create a prompt without document context
                var noContextPrompt = $@"Answer the following question to the best of your ability:

Question: {query}

Answer:";
                
                var noContextTokens = EstimateTokens(noContextPrompt);
                
                try
                {
                    // Use AI service to answer the question
                    var (_, aiResponse, wasAdvanced) = await _aiService.ProcessRequestAsync(noContextPrompt, cts.Token);
                    
                    return new QueryResult
                    {
                        Answer = aiResponse ?? "I couldn't generate a response.",
                        Sources = new List<DocumentSource>(),
                        UsedAdvancedModel = wasAdvanced,
                        ModelUsed = wasAdvanced ? "advanced" : "local",
                        TokenCount = noContextTokens
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing query without document context");
                    return new QueryResult
                    {
                        Answer = "I couldn't find any relevant documents, and encountered an error trying to answer your question. Please try rephrasing your query or upload relevant documents.",
                        Sources = new List<DocumentSource>(),
                        UsedAdvancedModel = false,
                        ModelUsed = "none",
                        TokenCount = 0
                    };
                }
            }

            // Build context from chunks (with token budgeting)
            try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "Retrieval", "Building context", 50); } } catch {}
            var context = await BuildContextFromChunksAsync(relevantChunks, groupIds);

            try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "Retrieval", "Context built", 60); } } catch {}

            // Estimate token count for the complete prompt
            var promptTemplate = $@"Based on the following context from documents, answer the question. 
If you reference specific information, mention which page it came from.

Context:
{context}

Question: {query}

Answer:";

            var estimatedTokens = EstimateTokens(promptTemplate);

            // Check if we should auto-delegate to advanced AI based on token count
            bool shouldDelegateToAdvancedAI = estimatedTokens > _maxLocalModelTokens;
            string delegationReason = shouldDelegateToAdvancedAI ? "token_threshold_exceeded" : "";

            _logger.LogInformation(
                "Query tokens: {Tokens}, Threshold: {Threshold}, Delegating: {Delegating}",
                estimatedTokens, _maxLocalModelTokens, shouldDelegateToAdvancedAI);

            // Generate response using AI with context
            string response;
            bool usedAdvancedModel;
            string modelUsed;

            if (shouldDelegateToAdvancedAI)
            {
                // Force use of advanced model via task dispatcher
                _logger.LogInformation("Auto-delegating to advanced AI due to token count: {Tokens}", estimatedTokens);

                var taskDispatcher = _serviceProvider.GetRequiredService<ITaskDispatcher>();
                var (handled, handlerResponse) = await taskDispatcher.TryHandleAsync("advanced", promptTemplate);
                if (handled && !string.IsNullOrEmpty(handlerResponse))
                {
                    response = handlerResponse;
                }
                else
                {
                    _logger.LogWarning("Advanced task handler not available, falling back to local model");
                    response = await GenerateResponseWithLocalModel(promptTemplate, cts.Token) ?? "I couldn't generate a response.";
                }

                usedAdvancedModel = true;
                modelUsed = "advanced";

                // Log the interaction
                await LogAIInteractionAsync(
                    promptTemplate,
                    response,
                    "advanced",
                    delegationReason,
                    estimatedTokens,
                    stopwatch.ElapsedMilliseconds,
                    groupIds?.FirstOrDefault());
                try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "AI", "Advanced model used", 95); } } catch {}
            }
            else
            {
                try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "AI", "Processing with local model", 70); } } catch {}
                var (_, aiResponse, wasAdvanced) = await _aiService.ProcessRequestAsync(promptTemplate, cts.Token);
                try { if (!string.IsNullOrEmpty(monitoringRequestId)) { _monitoringService?.UpdateRequestStatus(monitoringRequestId, "AI", "AI response received", 95); } } catch {}
                response = aiResponse ?? "I couldn't generate a response.";
                usedAdvancedModel = wasAdvanced;
                modelUsed = wasAdvanced ? "advanced" : "local";

                if (wasAdvanced)
                {
                    await LogAIInteractionAsync(
                        promptTemplate,
                        response,
                        modelUsed,
                        "ai_service_delegation",
                        estimatedTokens,
                        stopwatch.ElapsedMilliseconds,
                        groupIds?.FirstOrDefault());
                }
            }

            // Build sources list with position information
            var sources = relevantChunks.Select(c => new DocumentSource
            {
                DocumentId = c.DocumentMetadataId,
                FileName = c.DocumentMetadata.FileName,
                PageNumber = c.PageNumber,
                ChunkId = c.Id,
                ChunkText = c.ChunkText.Length > 200 ? c.ChunkText.Substring(0, 200) + "..." : c.ChunkText,
                X = c.X,
                Y = c.Y,
                Width = c.Width,
                Height = c.Height,
                Position = GetPositionDescription(c.X, c.Y)
            }).ToList();

            // Analyze response to identify which sources were actually referenced
            var referencedSources = IdentifyReferencedSources(response, sources);

            return new QueryResult
            {
                Answer = response,
                Sources = sources,
                ReferencedSources = referencedSources,
                UsedAdvancedModel = usedAdvancedModel,
                ModelUsed = modelUsed,
                TokenCount = estimatedTokens,
                DelegationReason = delegationReason
            };
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Query timed out after {Timeout}s for query: {Query}", _queryTimeoutSeconds, query);
            throw new TimeoutException($"Query operation timed out after {_queryTimeoutSeconds} seconds. Please try a simpler query or increase the timeout.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents with query: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// Identifies which sources were actually referenced in the AI response
    /// </summary>
    private List<DocumentSource> IdentifyReferencedSources(string response, List<DocumentSource> allSources)
    {
        var referenced = new List<DocumentSource>();

        if (string.IsNullOrEmpty(response))
        {
            return referenced;
        }

        foreach (var source in allSources)
        {
            // Check if the response mentions the file name or page number
            if (response.Contains(source.FileName, StringComparison.OrdinalIgnoreCase) ||
                response.Contains($"page {source.PageNumber}", StringComparison.OrdinalIgnoreCase) ||
                response.Contains($"Page {source.PageNumber}"))
            {
                referenced.Add(source);
            }
        }

        return referenced;
    }

    /// <summary>
    /// Logs AI interactions with advanced models for auditing
    /// </summary>
    private async Task LogAIInteractionAsync(
        string input,
        string output,
        string model,
        string delegationReason,
        int estimatedTokens,
        long responseTimeMs,
        Guid? groupId = null)
    {
        try
        {
            var log = new AIInteractionLog
            {
                Id = Guid.NewGuid(),
                Input = input.Length > 5000 ? input.Substring(0, 5000) + "..." : input,
                Output = output.Length > 5000 ? output.Substring(0, 5000) + "..." : output,
                ModelUsed = model,
                DelegationReason = delegationReason,
                InputTokens = estimatedTokens,
                OutputTokens = EstimateTokens(output),
                TotalTokens = estimatedTokens + EstimateTokens(output),
                Timestamp = DateTime.UtcNow,
                ResponseTimeMs = (int)responseTimeMs,
                ResourceGroupId = groupId,
                Success = true
            };

            _context.AIInteractionLogs.Add(log);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Logged AI interaction: Model={Model}, Tokens={Tokens}, ResponseTime={Ms}ms",
                model, log.TotalTokens, responseTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log AI interaction");
            // Don't throw - logging failure shouldn't break the main flow
        }
    }

    public async Task<QueryResult> QueryDocumentsWithConversationAsync(
        string query,
        Guid conversationId,
        int topK = 5,
        List<Guid>? groupIds = null,
        List<Guid>? documentIds = null)
    {
        try
        {
            // Classify the raw query FIRST before RAG wraps it in a long prompt.
            // This ensures task handlers (time, reminder, select-document, open-file, etc.)
            // are caught regardless of how the user phrases it.

            // Step 1: Zero-cost direct match (e.g. "select document" -> "select-document")
            var directTaskName = query.Trim().ToLower().Replace(" ", "-");
            var taskDispatcherEarly = _serviceProvider.GetRequiredService<ITaskDispatcher>();
            if (!taskDispatcherEarly.KnownTasks.Contains(directTaskName))
            {
                // Step 2: AI classify on raw input
                directTaskName = await _aiService.ClassifyOnlyAsync(query);
            }

            if (directTaskName != "unknown" && !string.IsNullOrEmpty(directTaskName))
            {
                var (handled, handlerResp) = await taskDispatcherEarly.TryHandleAsync(directTaskName, query);
                if (handled && !string.IsNullOrEmpty(handlerResp))
                {
                    return new QueryResult
                    {
                        Answer = handlerResp,
                        Sources = new List<DocumentSource>(),
                        UsedAdvancedModel = false,
                        ModelUsed = "local",
                        TokenCount = 0
                    };
                }
            }

            // Get recent conversation messages
            var recentMessages = await _context.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Timestamp)
                .Take(_conversationMessageLimit)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            // Search for relevant chunks
            var relevantChunks = await SearchChunksAsync(query, topK, groupIds, documentIds);

            // Build context from chunks (with token budgeting)
            var contextBuilder = new StringBuilder();
            bool hasDocumentContext = relevantChunks.Any();
            bool hasConversationContext = recentMessages.Any();

            // Add conversation history first (if it fits in budget)
            if (hasConversationContext)
            {
                contextBuilder.AppendLine("--- Recent Conversation ---");
                int historyTokens = 0;
                foreach (var msg in recentMessages)
                {
                    var msgText = $"{msg.Role}: {msg.Content}";
                    var msgTokens = EstimateTokens(msgText);

                    if (historyTokens + msgTokens > _maxContextTokens * _conversationHistoryPercentage / 100)
                        break;

                    contextBuilder.AppendLine(msgText);
                    historyTokens += msgTokens;
                }
                contextBuilder.AppendLine();
            }

            // Add document context (with remaining budget)
            var usedTokens = EstimateTokens(contextBuilder.ToString());
            var remainingBudget = _maxContextTokens - usedTokens;

            if (remainingBudget > 500 && hasDocumentContext)
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

            // Conditionally format the prompt based on available context
            string prompt;
            if (hasConversationContext || hasDocumentContext)
            {
                // Include context sections when we have relevant information
                prompt = $@"Based on the conversation history and document context, answer the question.

{contextBuilder}

Current Question: {query}

Answer:";
            }
            else
            {
                // Simple prompt without context when no relevant information found
                prompt = $@"Answer the following question to the best of your ability:

Question: {query}

Answer:";
            }

            // Call the model directly without task detection wrapper to preserve conversation context
            var response = await GenerateResponseWithLocalModel(prompt);

            // Build sources list with position information
            var sources = relevantChunks.Select(c => new DocumentSource
            {
                DocumentId = c.DocumentMetadataId,
                FileName = c.DocumentMetadata.FileName,
                PageNumber = c.PageNumber,
                ChunkId = c.Id,
                ChunkText = c.ChunkText.Length > 200 ? c.ChunkText.Substring(0, 200) + "..." : c.ChunkText,
                X = c.X,
                Y = c.Y,
                Width = c.Width,
                Height = c.Height,
                Position = GetPositionDescription(c.X, c.Y)
            }).ToList();

            // Build conversation sources list
            var conversationSources = recentMessages.Select(m => new ConversationSource
            {
                ConversationId = m.ConversationId,
                MessageId = m.Id,
                Role = m.Role,
                Content = m.Content.Length > 200 ? m.Content.Substring(0, 200) + "..." : m.Content,
                Timestamp = m.Timestamp
            }).ToList();

            return new QueryResult
            {
                Answer = response,
                Sources = sources,
                ConversationSources = conversationSources
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents with conversation context");
            throw;
        }
    }

    public async Task<List<DocumentChunk>> SearchChunksAsync(string query, int topK = 5, List<Guid>? groupIds = null, List<Guid>? documentIds = null)
    {
        try
        {
            // Get query embedding
            var queryEmbedding = await GenerateQueryEmbeddingAsync(query);

            List<DocumentChunk> results;

            if (_useVectorDB)
            {
                // Use pgvector for efficient similarity search with per-group indexing
                results = await SearchChunksWithVectorDBAsync(queryEmbedding, topK, groupIds, documentIds);
            }
            else
            {
                results = await SearchChunksInMemoryAsync(queryEmbedding, topK, groupIds, documentIds);
            }

            // If no embedding-based results, fallback to keyword search
            if (results.Count == 0)
            {
                return await KeywordSearchAsync(query, topK, groupIds, documentIds);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching chunks");

            // Fallback to keyword search
            return await KeywordSearchAsync(query, topK, groupIds, documentIds);
        }
    }

    /// <summary>
    /// Searches chunks using PostgreSQL pgvector for efficient vector similarity
    /// with per-group indexing support
    /// </summary>
    private async Task<List<DocumentChunk>> SearchChunksWithVectorDBAsync(
        float[] queryEmbedding,
        int topK,
        List<Guid>? groupIds,
        List<Guid>? documentIds)
    {
        try
        {
            var queryVector = new Vector(queryEmbedding);

            // Build base query
            var query = _context.DocumentChunks
                .Include(c => c.DocumentMetadata)
                .ThenInclude(d => d.DocumentGroups)
                .Where(c => c.EmbeddingVector != null);

            // Filter by groups if specified (per-group indexing)
            if (groupIds != null && groupIds.Any())
            {
                _logger.LogInformation("Filtering chunks by {Count} groups for vector search", groupIds.Count);
                query = query.Where(c =>
                    c.DocumentMetadata.DocumentGroups.Any(dg => groupIds.Contains(dg.ResourceGroupId)));
            }

            // Filter by document IDs if specified
            if (documentIds != null && documentIds.Any())
            {
                _logger.LogInformation("Filtering chunks by {Count} documents for vector search", documentIds.Count);
                query = query.Where(c => documentIds.Contains(c.DocumentMetadataId));
            }

            // Use pgvector for similarity search
            // Since EF Core pgvector extensions may be limited, we'll fetch candidates
            // and sort in-memory for now. In production, use proper vector indexes.
            var candidates = await query.ToListAsync();

            if (!candidates.Any())
            {
                _logger.LogWarning("No chunks with vector embeddings found");
                return new List<DocumentChunk>();
            }

            // Calculate cosine similarity in-memory
            var results = candidates
                .Select(chunk => new
                {
                    Chunk = chunk,
                    Score = CalculateCosineSimilarity(queryEmbedding, chunk.EmbeddingVector!.ToArray())
                })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Chunk)
                .ToList();

            _logger.LogInformation(
                "Vector DB search returned {Count} chunks (requested {TopK})",
                results.Count, topK);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in vector DB search, falling back to in-memory");
            return await SearchChunksInMemoryAsync(queryEmbedding, topK, groupIds, documentIds);
        }
    }

    /// <summary>
    /// Fallback in-memory search using JSON embeddings
    /// </summary>
    private async Task<List<DocumentChunk>> SearchChunksInMemoryAsync(
        float[] queryEmbedding,
        int topK,
        List<Guid>? groupIds,
        List<Guid>? documentIds)
    {
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

        // Filter by document IDs if specified
        if (documentIds != null && documentIds.Any())
        {
            chunksQuery = chunksQuery.Where(c => documentIds.Contains(c.DocumentMetadataId));
        }

        var allChunks = await chunksQuery.ToListAsync();

        if (!allChunks.Any())
        {
            _logger.LogWarning("No chunks with embeddings found");
            return new List<DocumentChunk>();
        }

        // Calculate similarity scores in-memory
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

        // Process chunks in batches with controlled concurrency
        var batches = chunks.Chunk(_embeddingBatchSize).ToList();
        _logger.LogInformation("Processing {ChunkCount} chunks in {BatchCount} batches (batch size: {BatchSize}, concurrency: {Concurrency})",
            chunks.Count, batches.Count, _embeddingBatchSize, _embeddingMaxConcurrency);

        var semaphore = new SemaphoreSlim(_embeddingMaxConcurrency);

        foreach (var batch in batches)
        {
            var tasks = batch.Select(async chunk =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var success = await GenerateEmbeddingWithRetryAsync(chunk);
                    if (!success)
                    {
                        Interlocked.Increment(ref failedChunks);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // Save progress after each batch
            await _context.SaveChangesAsync();
            _logger.LogDebug("Completed batch with {Success} successes, {Failed} failures",
                batch.Count() - tasks.Count(t => t.IsFaulted), tasks.Count(t => t.IsFaulted));
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

    public async Task<int> ReindexAllDocumentsAsync(bool force = false)
    {
        var docsQuery = _context.DocumentMetadatas.AsQueryable();
        // Verify the DB vector column dimension matches the configured embedding dimension.
        // Do NOT attempt to ALTER to a smaller dimension — that would nullify all Lambda-stored
        // 1024-dim vectors. Only ALTER if the column currently has a smaller type.
        try
        {
            var currentDim = await _context.Database
                .SqlQueryRaw<int>(
                    "SELECT (regexp_match(atttypmod::text, '\\d+'))[1]::int AS \"Value\"" +
                    " FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid" +
                    " WHERE c.relname = 'DocumentChunks'" +
                    " AND a.attname = 'EmbeddingVector'" +
                    " AND a.attnum > 0")
                .FirstOrDefaultAsync();
            if (currentDim > 0 && currentDim != _embeddingDimension)
            {
                _logger.LogInformation("EmbeddingVector column is vector({Current}), altering to vector({Target})", currentDim, _embeddingDimension);
                await _context.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE \"DocumentChunks\" ALTER COLUMN \"EmbeddingVector\" TYPE vector({_embeddingDimension}) USING NULL;");
            }
            else
            {
                _logger.LogInformation("EmbeddingVector column is already vector({Dim}), no ALTER needed", _embeddingDimension);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify/alter EmbeddingVector column dimension; proceeding with reindex");
        }
        if (!force)
        {
            // Only reindex documents that are not processed or have errors
            docsQuery = docsQuery.Where(d => !d.IsProcessed || d.ProcessingError != null);
        }

        var docs = await docsQuery.ToListAsync();
        var reindexed = 0;

        foreach (var doc in docs)
        {
            try
            {
                _logger.LogInformation("Reindexing document {DocumentId} ({FileName})", doc.Id, doc.FileName);
                await GenerateEmbeddingsAsync(doc.Id);
                doc.IsProcessed = true;
                doc.ProcessedAt = DateTime.UtcNow;
                doc.ProcessingError = null;
                reindexed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reindex document {DocumentId}", doc.Id);
                doc.ProcessingError = ex.Message;
            }
            // persist progress after each document
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Reindexing complete. Reindexed {Count} documents (force={Force})", reindexed, force);
        return reindexed;
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

                // Store as JSON embedding and record dimension
                chunk.Embedding = SerializeEmbedding(embedding);
                chunk.EmbeddingDimension = embedding.Length;

                // Always write to the pgvector column. The embedding dimension is determined by the
                // configured model (AWS:Bedrock:EmbeddingDimensions, default 1024) and must match the
                // DB column type set by the SwitchToBedrockEmbeddings_vector1024 migration.
                if (embedding.Length != _embeddingDimension)
                {
                    _logger.LogWarning("Embedding dim {Dim} does not match configured DB dim {Expected} for chunk {ChunkId} — storing anyway", embedding.Length, _embeddingDimension, chunk.Id);
                }
                chunk.EmbeddingVector = new Vector(embedding);

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
            _logger.LogInformation("Attempting to generate embedding for text (length: {Length})", text.Length);
            
            // Try to use embedding client if registered in DI
            try
            {
                var client = _serviceProvider.GetService(typeof(IEmbeddingClient)) as IEmbeddingClient;
                if (client != null)
                {
                    _logger.LogInformation("Using IEmbeddingClient for embeddings");
                    var sw = Stopwatch.StartNew();
                    var embedding = await client.GenerateEmbeddingAsync(text);
                    sw.Stop();
                    _logger.LogInformation("Successfully generated embedding (dimension: {Dimension}) in {Ms}ms", embedding.Length, sw.ElapsedMilliseconds);
                    return embedding;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IEmbeddingClient failed, will fallback to kernel/service-based embedding");
            }

            var embeddingService = _kernel.Services.GetService(typeof(IEmbeddingGenerator<string, Embedding<float>>))
                as IEmbeddingGenerator<string, Embedding<float>>;

            if (embeddingService != null)
            {
                _logger.LogInformation("Embedding service found in kernel, calling it for embedding generation...");
                try
                {
                    var sw = Stopwatch.StartNew();
                    var embedding = await embeddingService.GenerateAsync(text);
                    sw.Stop();
                    var result = embedding.Vector.Span.ToArray();
                    _logger.LogInformation("Successfully generated embedding from kernel service (dimension: {Dimension}) in {Ms}ms", result.Length, sw.ElapsedMilliseconds);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Embedding service call threw an exception: {Message}", ex.Message);
                    throw;
                }
            }
            else
            {
                _logger.LogWarning("No embedding service registered in Kernel, falling back to simple embedding");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding service failed: {Message}. Falling back to simple embedding", ex.Message);
        }

        // Fallback: simple hash-based embedding (for demonstration)
        // In production, use a proper embedding model
        _logger.LogInformation("Using simple hash-based embedding fallback");
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

    private async Task<List<DocumentChunk>> KeywordSearchAsync(string query, int topK, List<Guid>? groupIds = null, List<Guid>? documentIds = null)
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

        // Filter by document IDs if specified
        if (documentIds != null && documentIds.Any())
        {
            chunksQuery = chunksQuery.Where(c => documentIds.Contains(c.DocumentMetadataId));
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
                .OrderByDescending(p => p.IsPinned) // Prioritize pinned prompts
                .ThenBy(p => p.CreatedAt)
                .ToListAsync();

            if (groupPrompts.Any())
            {
                var groupContextHeader = "--- Additional Context from Groups ---";
                var headerTokens = EstimateTokens(groupContextHeader);

                if (currentTokens + headerTokens < _maxContextTokens)
                {
                    context.AppendLine(groupContextHeader);
                    currentTokens += headerTokens;

                    // Add pinned prompts first (they get priority)
                    foreach (var prompt in groupPrompts)
                    {
                        var promptHeader = $"[{prompt.Name}]" + (prompt.IsPinned ? " [PINNED]:" : ":");
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

    private async Task<string> GenerateResponseWithLocalModel(string prompt, CancellationToken cancellationToken = default)
    {
            try
            {
            _logger.LogInformation("Generating response with AI model...");
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();
            _logger.LogInformation("Chat service obtained, sending request...");
            var sw = Stopwatch.StartNew();
            var result = await chatService.GetChatMessageContentAsync(prompt, cancellationToken: cancellationToken);
            sw.Stop();
            _logger.LogInformation("Successfully received response (length: {Length}) in {Ms}ms", result.Content?.Length ?? 0, sw.ElapsedMilliseconds);
            return result.Content ?? "No response from model.";
            }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Model generation was cancelled/timed out");
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error connecting to AI service: {Message}", ex.Message);
            return $"Error: Cannot connect to AI service. Please ensure the service is running. Details: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response with local model: {Message}", ex.Message);
            return $"Error: {ex.Message}";
        }
    }

    public async Task<(string embeddingJson, int dimension)> GenerateTextEmbeddingJsonAsync(string text)
    {
        try
        {
            var embedding = await GenerateTextEmbeddingAsync(text);
            var json = SerializeEmbedding(embedding);
            return (json, embedding.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for text");
            throw;
        }
    }

    public async Task<QueryResult> QueryRemindersAsync(string query, int topK = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_queryTimeoutSeconds));
        try
        {
            _logger.LogInformation("Querying reminders with query: {Query}", query);

            // Get all reminders
            var reminders = await _context.RequestHistories
                .Where(h => h.RequestType == "reminder")
                .OrderByDescending(h => h.Timestamp)
                .Take(topK * 2) // Get more to allow for filtering
                .ToListAsync();

            if (!reminders.Any())
            {
                return new QueryResult
                {
                    Answer = "You don't have any reminders saved.",
                    Sources = new List<DocumentSource>(),
                    UsedAdvancedModel = false,
                    ModelUsed = "none",
                    TokenCount = 0
                };
            }

            // Simple keyword matching for reminders
            var relevantReminders = reminders
                .Where(r =>
                    r.InputText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    query.Split(' ').Any(word =>
                        word.Length > 3 && r.InputText.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Take(topK)
                .ToList();

            // If no keyword matches, return most recent reminders
            if (!relevantReminders.Any())
            {
                relevantReminders = reminders.Take(topK).ToList();
            }

            // Build context from reminders
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Your Reminders:");
            contextBuilder.AppendLine();

            var sources = new List<DocumentSource>();
            foreach (var reminder in relevantReminders)
            {
                contextBuilder.AppendLine($"- {reminder.InputText}");
                contextBuilder.AppendLine($"  (Created: {reminder.Timestamp:yyyy-MM-dd HH:mm})");
                contextBuilder.AppendLine();

                sources.Add(new DocumentSource
                {
                    DocumentId = reminder.Id,
                    FileName = reminder.InputText.Length > 30
                        ? $"Reminder: {reminder.InputText.Substring(0, 30)}..."
                        : $"Reminder: {reminder.InputText}",
                    PageNumber = 0,
                    ChunkId = Guid.Empty,
                    ChunkText = reminder.InputText
                });
            }

            var context = contextBuilder.ToString();

            // Generate AI response about the reminders
            var prompt = $@"Based on the user's reminders listed below, answer their question.
Be helpful and conversational. If they ask about a specific reminder, provide details.
If they ask for all reminders, list them clearly.

{context}

User Question: {query}

Answer:";

            var answer = await GenerateResponseWithLocalModel(prompt, cts.Token);

            return new QueryResult
            {
                Answer = answer,
                Sources = sources,
                UsedAdvancedModel = false,
                ModelUsed = "local-model",
                TokenCount = EstimateTokens(prompt + answer)
            };
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Reminder query timed out after {Timeout}s", _queryTimeoutSeconds);
            return new QueryResult
            {
                Answer = $"Query timed out after {_queryTimeoutSeconds} seconds. Please try a simpler query.",
                Sources = new List<DocumentSource>(),
                UsedAdvancedModel = false,
                ModelUsed = "none",
                TokenCount = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying reminders");
            return new QueryResult
            {
                Answer = $"Error searching reminders: {ex.Message}",
                Sources = new List<DocumentSource>(),
                UsedAdvancedModel = false,
                ModelUsed = "none",
                TokenCount = 0
            };
        }
    }

    public async Task<QueryResult> QueryDocumentsAndRemindersAsync(string query, int topK = 5, List<Guid>? groupIds = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_queryTimeoutSeconds));
        try
        {
            _logger.LogInformation("Querying documents and reminders with query: {Query}", query);

            // Check if query is reminder-specific
            var isReminderQuery = query.Contains("reminder", StringComparison.OrdinalIgnoreCase) ||
                                 query.Contains("remind me", StringComparison.OrdinalIgnoreCase) ||
                                 query.Contains("what did i", StringComparison.OrdinalIgnoreCase);

            // Query both documents and reminders
            var documentTask = QueryDocumentsAsync(query, topK, groupIds, null, null);
            var reminderTask = QueryRemindersAsync(query, topK);

            await Task.WhenAll(documentTask, reminderTask);

            var documentResult = await documentTask;
            var reminderResult = await reminderTask;

            // If it's a reminder-specific query, prioritize reminders
            if (isReminderQuery && reminderResult.Sources.Any())
            {
                return reminderResult;
            }

            // Combine results if both have content
            if (documentResult.Sources.Any() && reminderResult.Sources.Any())
            {
                var combinedSources = new List<DocumentSource>();
                combinedSources.AddRange(reminderResult.Sources);
                combinedSources.AddRange(documentResult.Sources.Take(Math.Max(0, topK - reminderResult.Sources.Count)));

                var contextBuilder = new StringBuilder();

                if (reminderResult.Sources.Any())
                {
                    contextBuilder.AppendLine("YOUR REMINDERS:");
                    foreach (var source in reminderResult.Sources)
                    {
                        contextBuilder.AppendLine($"- {source.ChunkText}");
                    }
                    contextBuilder.AppendLine();
                }

                if (documentResult.Sources.Any())
                {
                    contextBuilder.AppendLine("DOCUMENT INFORMATION:");
                    foreach (var source in documentResult.Sources.Take(topK - reminderResult.Sources.Count))
                    {
                        contextBuilder.AppendLine($"From {source.FileName} (Page {source.PageNumber}):");
                        contextBuilder.AppendLine(source.ChunkText);
                        contextBuilder.AppendLine();
                    }
                }

                var combinedPrompt = $@"Based on the following information from both reminders and documents, answer the question.

{contextBuilder}

Question: {query}

Answer:";

                var answer = await GenerateResponseWithLocalModel(combinedPrompt, cts.Token);

                return new QueryResult
                {
                    Answer = answer,
                    Sources = combinedSources,
                    UsedAdvancedModel = false,
                    ModelUsed = "local-model",
                    TokenCount = EstimateTokens(combinedPrompt + answer)
                };
            }

            // Return whichever has content, preferring documents
            if (documentResult.Sources.Any())
            {
                return documentResult;
            }

            return reminderResult;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Combined query timed out after {Timeout}s", _queryTimeoutSeconds);
            return new QueryResult
            {
                Answer = $"Query timed out after {_queryTimeoutSeconds} seconds. Please try a simpler query.",
                Sources = new List<DocumentSource>(),
                UsedAdvancedModel = false,
                ModelUsed = "none",
                TokenCount = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents and reminders");
            return new QueryResult
            {
                Answer = $"Error searching: {ex.Message}",
                Sources = new List<DocumentSource>(),
                UsedAdvancedModel = false,
                ModelUsed = "none",
                TokenCount = 0
            };
        }
    }

    private string? GetPositionDescription(double? x, double? y)
    {
        if (!x.HasValue || !y.HasValue)
            return null;

        // Determine horizontal position
        string horizontal;
        if (x.Value < PdfConstants.PageWidth / 3)
            horizontal = "left";
        else if (x.Value < 2 * PdfConstants.PageWidth / 3)
            horizontal = "center";
        else
            horizontal = "right";

        // Determine vertical position (Y is from bottom in PDF coordinates)
        string vertical;
        if (y.Value < PdfConstants.PageHeight / 3)
            vertical = "bottom";
        else if (y.Value < 2 * PdfConstants.PageHeight / 3)
            vertical = "middle";
        else
            vertical = "top";

        return $"{vertical} {horizontal}";
    }
}
