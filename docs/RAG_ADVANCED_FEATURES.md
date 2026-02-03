# RAG Pipeline - Advanced Features Summary

This document summarizes the advanced features and improvements made to the RAG (Retrieval-Augmented Generation) pipeline.

## Overview of Enhancements

The RAG pipeline has been significantly enhanced with production-ready features including:

1. **Structured Response Format**
2. **Deterministic Embeddings**
3. **Embedding Dimension Validation**
4. **EF Core Compatible Search**
5. **Processing State Tracking**
6. **Token Budget Control**
7. **Conversation System**
8. **Conversation History Integration**

## 1. Structured Response Format

### What Changed
- `QueryDocumentsAsync` now returns `QueryResult` instead of `string`
- Response includes both `Answer` and `Sources` list

### QueryResult Model
```csharp
public class QueryResult
{
    public string Answer { get; set; }
    public List<DocumentSource> Sources { get; set; }
}

public class DocumentSource
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; }
    public int PageNumber { get; set; }
    public Guid ChunkId { get; set; }
    public string ChunkText { get; set; } // Preview (truncated to 200 chars)
}
```

### Benefits
- API responses match documented behavior
- Clients can display source attribution
- Better user experience with citations

### API Example
```bash
curl -X POST http://localhost:5000/api/documents/query \
  -H "Content-Type: application/json" \
  -d '{"query": "What is discussed?"}'

# Response:
{
  "answer": "The document discusses...",
  "sources": [
    {
      "documentId": "guid",
      "fileName": "document.pdf",
      "pageNumber": 1,
      "chunkId": "guid",
      "chunkText": "Preview of relevant text..."
    }
  ]
}
```

## 2. Deterministic SHA256 Embeddings

### What Changed
- Replaced `Random`-based embedding with SHA256 hashing
- Embeddings are now deterministic (same text = same embedding)

### Implementation
```csharp
private float[] GenerateSimpleEmbedding(string text)
{
    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(UTF8.GetBytes(text));
    
    // Generate 384-dimension vector by iteratively hashing
    // Map bytes to floats in range [-1, 1]
    // Normalize the vector
}
```

### Benefits
- Consistent embeddings across restarts
- Reproducible results for testing
- More reliable fallback when embedding service unavailable

### Note
Still recommend using proper embedding models (OpenAI, Sentence Transformers) in production.

## 3. Embedding Dimension Validation

### What Changed
- Added `EmbeddingDimension` field to `DocumentChunk`
- Store dimension when generating embeddings
- Validate dimensions before cosine similarity calculation

### Database Schema
```sql
ALTER TABLE DocumentChunk 
ADD COLUMN EmbeddingDimension INT NULL;
```

### Validation Logic
```csharp
private float CalculateCosineSimilarity(float[] a, float[] b)
{
    if (a.Length == 0 || b.Length == 0) return 0;
    if (a.Length != b.Length)
    {
        _logger.LogWarning("Dimension mismatch: {A} vs {B}", a.Length, b.Length);
        return 0;
    }
    // ... calculate similarity
}
```

### Benefits
- Prevents runtime errors from dimension mismatches
- Logging helps diagnose embedding issues
- Supports mixed embedding models

## 4. EF Core Compatible Keyword Search

### What Changed
- Replaced `keywords.Any(k => text.Contains(k))` with EF-translatable predicates
- Uses `EF.Functions.Like` for database-level filtering
- In-memory scoring after fetching candidates

### Implementation
```csharp
private async Task<List<DocumentChunk>> KeywordSearchAsync(...)
{
    // Use EF.Functions.Like for database filtering
    foreach (var keyword in keywords)
    {
        var query = _context.DocumentChunks
            .Where(c => EF.Functions.Like(c.ChunkText.ToLower(), $"%{keyword}%"));
        // Union queries
    }
    
    // Fetch candidates
    var chunks = await query.Take(topK * 3).ToListAsync();
    
    // Score in-memory
    return chunks.OrderByDescending(c => score).Take(topK);
}
```

### Benefits
- No EF Core translation errors
- Better database performance
- Supports complex filtering

## 5. Processing State Tracking

### What Changed
- `GenerateEmbeddingsAsync` now updates document processing state
- Three states: complete success, partial success, complete failure
- `ProcessingError` includes detailed failure information

### State Logic
```csharp
if (failedChunks == 0)
{
    document.IsProcessed = true;
    document.ProcessingError = null;
}
else if (failedChunks < totalChunks)
{
    document.IsProcessed = true;
    document.ProcessingError = $"Failed {failedChunks} of {totalChunks} chunks";
}
else
{
    document.IsProcessed = false;
    document.ProcessingError = "Failed all chunks";
}
```

### Benefits
- Accurate processing status
- Actionable error messages
- Better monitoring and debugging

## 6. Token Budget Control

### What Changed
- Added `MaxContextTokens` and `ApproximateTokensPerChar` configuration
- Context building respects token budgets
- Truncates or excludes chunks to fit budget

### Configuration
```json
{
  "RAG": {
    "MaxContextTokens": 4000,
    "ApproximateTokensPerChar": 0.25
  }
}
```

### Implementation
```csharp
private async Task<string> BuildContextFromChunksAsync(...)
{
    int currentTokens = 0;
    foreach (var chunk in chunks)
    {
        var chunkTokens = EstimateTokens(chunk.ChunkText);
        
        if (currentTokens + chunkTokens > _maxContextTokens)
        {
            // Try to fit truncated chunk or stop
            break;
        }
        
        // Add chunk to context
        currentTokens += chunkTokens;
    }
}
```

### Benefits
- Prevents context overflow errors
- Cost control for API usage
- Prioritizes most relevant chunks
- Logging shows what was included/excluded

## 7. Conversation System

### What Changed
- Created `Conversation` and `ChatMessage` models
- Implemented `ConversationService` for conversation management
- Added `ConversationsController` REST API

### Database Schema
```sql
CREATE TABLE Conversation (
    Id UUID PRIMARY KEY,
    Title VARCHAR(500),
    CreatedAt TIMESTAMP,
    UpdatedAt TIMESTAMP,
    IsActive BOOLEAN,
    ResourceGroupId UUID,
    Summary TEXT
);

CREATE TABLE ChatMessage (
    Id UUID PRIMARY KEY,
    ConversationId UUID FOREIGN KEY,
    Role VARCHAR(50), -- 'user', 'assistant', 'system'
    Content TEXT,
    Model VARCHAR(255),
    Timestamp TIMESTAMP,
    TokenCount INT
);
```

### ConversationService Features
- **CreateConversationAsync** - Create new conversations
- **GetConversationAsync** - Retrieve with all messages
- **GetActiveConversationsAsync** - List active conversations
- **AddMessageAsync** - Add messages (user/assistant/system)
- **DetectOrCreateConversationAsync** - Auto-detect or create
- **SummarizeConversationAsync** - Generate summaries
- **ArchiveConversationAsync** - Archive old conversations

### API Endpoints
```
POST   /api/conversations                    - Create conversation
GET    /api/conversations                    - List active conversations
GET    /api/conversations/{id}               - Get conversation
POST   /api/conversations/{id}/messages      - Add message
POST   /api/conversations/{id}/query         - Query with context
POST   /api/conversations/{id}/summarize     - Summarize
POST   /api/conversations/{id}/archive       - Archive
```

### Benefits
- Organized conversation threads
- Linked to ResourceGroups
- Persistent across restarts
- Auto-detection for seamless UX

## 8. Conversation History Integration

### What Changed
- Added `QueryDocumentsWithConversationAsync` method
- Includes recent conversation messages in RAG queries
- Token budget split between history and documents

### Implementation
```csharp
public async Task<QueryResult> QueryDocumentsWithConversationAsync(...)
{
    // Get last 5 messages
    var recentMessages = await GetRecentMessages(conversationId, 5);
    
    // Reserve 25% of token budget for conversation history
    var historyBudget = _maxContextTokens / 4;
    
    // Add conversation history to context
    foreach (var msg in recentMessages)
    {
        if (historyTokens + msgTokens > historyBudget) break;
        context.AppendLine($"{msg.Role}: {msg.Content}");
    }
    
    // Add document chunks with remaining budget
    // ... (similar to BuildContextFromChunksAsync)
}
```

### Usage Flow
1. User sends message via `/api/conversations/{id}/query`
2. User message added to conversation
3. System retrieves last 5 messages for context
4. Searches relevant documents
5. Builds prompt with history (25%) + documents (75%)
6. Generates response
7. Assistant response added to conversation

### Benefits
- AI remembers previous conversation
- Contextual awareness
- Natural multi-turn conversations
- Token budget prevents overflow

## Auto-Detection and Reference

### Auto-Detect Conversations
```csharp
public async Task<Conversation> DetectOrCreateConversationAsync(...)
{
    // Check for active conversation in last hour with same group
    var recent = await _context.Conversations
        .Where(c => c.IsActive && 
                    c.UpdatedAt > DateTime.UtcNow.AddHours(-1) &&
                    c.ResourceGroupId == groupId)
        .OrderByDescending(c => c.UpdatedAt)
        .FirstOrDefaultAsync();
    
    if (recent != null) return recent; // Continue existing
    
    // Create new conversation with title from first message
    return await CreateConversationAsync(titleFromMessage, groupId);
}
```

### Reference Old Conversations
```csharp
// Get all conversations for a group
GET /api/conversations?groupId={guid}

// Get specific conversation with full history
GET /api/conversations/{id}
```

## Migration Guide

### Database Updates
The application uses `EnsureCreated()`, so new tables will be created automatically on first run:
- `Conversation` table
- `ChatMessage` table
- `EmbeddingDimension` column in `DocumentChunk`

### Configuration Updates
Add to `appsettings.json`:
```json
{
  "RAG": {
    "MaxContextTokens": 4000,
    "ApproximateTokensPerChar": 0.25
  }
}
```

### API Changes
- `POST /api/documents/query` now returns `QueryResult` instead of string
- Update clients to use `result.answer` and `result.sources`

### New APIs
- Conversation APIs available at `/api/conversations`
- Use for conversational RAG queries

## Best Practices

### Token Budgeting
- Adjust `MaxContextTokens` based on your AI model limits
- Fine-tune `ApproximateTokensPerChar` for accuracy
- Monitor logs to see chunk inclusion/exclusion

### Conversations
- Link conversations to ResourceGroups for context
- Use auto-detection for seamless UX
- Summarize long conversations periodically
- Archive old conversations to keep DB clean

### Embeddings
- Use proper embedding models in production
- SHA256 fallback is for development/testing
- Validate dimension consistency
- Monitor processing state for failures

### Processing State
- Check `/api/documents/unprocessed` regularly
- Use `/api/documents/reprocess` to retry failures
- Review `ProcessingError` for debugging

## Performance Considerations

1. **Token Estimation**: Rough approximation, adjust for accuracy
2. **Conversation History**: Limited to 5 recent messages for performance
3. **Keyword Search**: Fetches 3x candidates, scores in-memory
4. **Chunk Budget**: Processes chunks in order, stops at limit

## Future Enhancements

Potential improvements:
1. **Batch Embedding Generation**: Process multiple chunks concurrently
2. **Vector Database**: Move to dedicated vector DB for scale
3. **Smart Summarization**: Use AI to summarize conversations
4. **Per-Group Indexing**: Separate indexes for better performance
5. **Conversation Branching**: Support conversation forks
6. **Message Editing**: Edit/delete messages in conversations

## Troubleshooting

### Context Too Long Errors
- Reduce `MaxContextTokens`
- Reduce `TopK` results
- Check `ApproximateTokensPerChar` calibration

### Dimension Mismatch Errors
- Check logs for specific dimensions
- Reprocess documents with consistent embedding model
- Validate `EmbeddingDimension` values

### Conversation Not Found
- Verify conversation `IsActive = true`
- Check `ResourceGroupId` filter
- Look for archived conversations

### Processing Failures
- Check `/api/documents/unprocessed`
- Review `ProcessingError` messages
- Verify embedding service availability
- Use `/api/documents/reprocess` to retry

## Summary

These enhancements make the RAG pipeline production-ready with:
- ✅ Structured responses for better UX
- ✅ Deterministic embeddings for reliability
- ✅ Dimension validation for safety
- ✅ EF-compatible search for performance
- ✅ Accurate processing state tracking
- ✅ Token budget control for cost management
- ✅ Full conversation system for contextual AI
- ✅ Conversation history integration for natural dialogue

The system is now ready for real-world conversational AI applications with document retrieval and generation.
