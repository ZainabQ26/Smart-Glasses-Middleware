# Vector DB, Per-Group Indexing, and AI Delegation

This document describes the vector database implementation, per-group indexing capabilities, auto-delegation to advanced AI, and AI interaction logging.

## Overview

The system now includes:
1. **PostgreSQL pgvector** for efficient vector similarity search
2. **Per-group indexing** for filtering vectors by ResourceGroup
3. **Auto-delegation** to advanced AI when token thresholds are exceeded
4. **AI interaction logging** for tracking advanced model usage
5. **Referenced source tracking** to identify what the AI actually used

## Vector Database with pgvector

### Setup

1. **Install pgvector extension** in PostgreSQL:
```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

2. **Database schema** includes:
```sql
ALTER TABLE "DocumentChunks" 
ADD COLUMN "EmbeddingVector" vector(384);

CREATE INDEX ON "DocumentChunks" 
USING ivfflat ("EmbeddingVector" vector_cosine_ops);
```

### Configuration

```json
{
  "RAG": {
    "UseVectorDB": true,
    "VectorSimilarityThreshold": 0.7,
    "EmbeddingBatchSize": 10,
    "EmbeddingMaxConcurrency": 5
  }
}
```

### How It Works

When `UseVectorDB` is enabled:
1. Embeddings are stored in both formats:
   - `EmbeddingVector` (pgvector Vector type) for fast DB queries
   - `Embedding` (JSON string) for backward compatibility
   
2. Similarity search uses PostgreSQL's vector operations:
   - Cosine distance calculation happens in the database
   - Results are filtered by group membership before similarity calculation
   - Top-K results returned efficiently

3. Fallback to in-memory search if vector DB fails

### Benefits

- **10-100x faster** for large document collections
- **Scalable** to millions of document chunks
- **Per-group filtering** applied at database level
- **Native PostgreSQL** - no external services required

## Per-Group Vector Indexing

### Purpose

Allow users to scope vector searches to specific ResourceGroups for better relevance and access control.

### Implementation

```csharp
// Search only within specified groups
var results = await ragService.SearchChunksAsync(
    query: "What is the budget?",
    topK: 5,
    groupIds: new List<Guid> { projectGroupId }
);
```

### Database Query

```sql
-- Vector search scoped to groups
SELECT dc.* 
FROM "DocumentChunks" dc
INNER JOIN "DocumentMetadatas" dm ON dc."DocumentMetadataId" = dm."Id"
INNER JOIN "DocumentGroups" dg ON dm."Id" = dg."DocumentMetadataId"
WHERE dg."ResourceGroupId" IN (@groupIds)
  AND dc."EmbeddingVector" IS NOT NULL
ORDER BY dc."EmbeddingVector" <=> @queryVector::vector
LIMIT @topK;
```

### Use Cases

1. **Multi-tenant applications** - Isolate searches per organization
2. **Project-based** - Search only project-specific documents
3. **Access control** - Respect group-based permissions
4. **Relevance** - Higher quality results from focused corpus

## Auto-Delegation to Advanced AI

### Configuration

```json
{
  "RAG": {
    "MaxLocalModelTokens": 2000,
    "MaxContextTokens": 4000
  }
}
```

### How It Works

1. **Token estimation** - Before processing, estimate total tokens:
```
Tokens = (Context + Query + Prompt Template) * 0.25 chars/token
```

2. **Delegation decision**:
```
IF estimatedTokens > MaxLocalModelTokens THEN
    Delegate to Advanced AI
    Log reason: "token_threshold_exceeded"
ELSE
    Process with local model
END IF
```

3. **Automatic logging** - All advanced AI calls are logged

### Example Flow

```
User Query: "Summarize all quarterly reports"
↓
Context Build: 15 document chunks, 8000 characters
↓
Token Estimate: 2500 tokens (8000 * 0.25 + 500 for prompt)
↓
Check Threshold: 2500 > 2000 (MaxLocalModelTokens)
↓
AUTO-DELEGATE to Advanced AI
↓
Log Interaction: model=advanced, reason=token_threshold_exceeded
↓
Return Enhanced Response
```

### Benefits

- **Cost optimization** - Use expensive models only when needed
- **Quality** - Complex queries get better models automatically
- **Transparency** - Clear logging of why delegation occurred

## AI Interaction Logging

### Purpose

Track all interactions with advanced AI models for:
- Cost monitoring
- Usage analytics
- Debugging
- Compliance/audit trails

### Data Captured

```csharp
public class AIInteractionLog
{
    public Guid Id { get; set; }
    public string Input { get; set; }           // What was sent
    public string Output { get; set; }          // What was returned
    public string ModelUsed { get; set; }       // e.g., "gpt-4", "advanced"
    public string DelegationReason { get; set; } // Why advanced model was used
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
    public DateTime Timestamp { get; set; }
    public int ResponseTimeMs { get; set; }
    public Guid? ResourceGroupId { get; set; }   // Optional context
    public Guid? ConversationId { get; set; }    // Optional context
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### API Endpoints

#### List Interactions
```http
GET /api/ai-interactions?groupId={guid}&model=advanced&limit=50&offset=0
```

Response:
```json
[
  {
    "id": "guid",
    "modelUsed": "advanced",
    "delegationReason": "token_threshold_exceeded",
    "inputTokens": 2500,
    "outputTokens": 800,
    "totalTokens": 3300,
    "estimatedCost": 0.05,
    "timestamp": "2026-02-03T16:00:00Z",
    "responseTimeMs": 3500,
    "success": true,
    "resourceGroupName": "Project Alpha",
    "conversationTitle": "Budget Discussion",
    "inputPreview": "Based on the following context...",
    "outputPreview": "The quarterly budget shows..."
  }
]
```

#### Get Interaction Details
```http
GET /api/ai-interactions/{id}
```

Returns full input and output (not truncated).

#### Get Usage Statistics
```http
GET /api/ai-interactions/stats?startDate=2026-02-01&endDate=2026-02-03&groupId={guid}
```

Response:
```json
{
  "modelStats": [
    {
      "model": "advanced",
      "count": 45,
      "totalTokens": 125000,
      "avgResponseTimeMs": 2800,
      "totalCost": 6.25,
      "successRate": 98.5
    },
    {
      "model": "local",
      "count": 320,
      "totalTokens": 180000,
      "avgResponseTimeMs": 450,
      "totalCost": 0,
      "successRate": 100
    }
  ],
  "delegationReasons": [
    {
      "reason": "token_threshold_exceeded",
      "count": 32
    },
    {
      "reason": "ai_service_delegation",
      "count": 13
    }
  ],
  "totalInteractions": 365
}
```

## Referenced Source Tracking

### Purpose

Identify which sources the AI actually referenced in its answer versus which sources were merely provided as context.

### Implementation

```csharp
private List<DocumentSource> IdentifyReferencedSources(string response, List<DocumentSource> allSources)
{
    var referenced = new List<DocumentSource>();
    
    foreach (var source in allSources)
    {
        // Check if response mentions the file name or page number
        if (response.Contains(source.FileName, StringComparison.OrdinalIgnoreCase) ||
            response.Contains($"page {source.PageNumber}", StringComparison.OrdinalIgnoreCase))
        {
            referenced.Add(source);
        }
    }
    
    return referenced;
}
```

### Response Format

```json
{
  "answer": "According to budget-2026.pdf page 5, the Q1 allocation is $2M...",
  "sources": [
    {"fileName": "budget-2026.pdf", "pageNumber": 5, ...},
    {"fileName": "budget-2025.pdf", "pageNumber": 3, ...},
    {"fileName": "forecast.docx", "pageNumber": 1, ...}
  ],
  "referencedSources": [
    {"fileName": "budget-2026.pdf", "pageNumber": 5, ...}
  ],
  "usedAdvancedModel": true,
  "modelUsed": "advanced",
  "tokenCount": 2500,
  "delegationReason": "token_threshold_exceeded"
}
```

### Benefits

- **Citation accuracy** - See what AI actually used
- **Source verification** - Validate AI's references
- **Quality metrics** - Track how often AI uses provided context
- **User transparency** - Show users exact sources

## Migration Guide

### Database Migration

1. **Enable pgvector extension**:
```sql
-- Run as database superuser
CREATE EXTENSION IF NOT EXISTS vector;
```

2. **Add vector column** (done automatically by EF Core):
```sql
ALTER TABLE "DocumentChunks" 
ADD COLUMN "EmbeddingVector" vector(384);
```

3. **Create vector index**:
```sql
-- After populating vectors
CREATE INDEX "IX_DocumentChunks_EmbeddingVector" 
ON "DocumentChunks" 
USING ivfflat ("EmbeddingVector" vector_cosine_ops)
WITH (lists = 100);
```

4. **Populate existing embeddings**:
```csharp
// Reprocess all documents to generate vector embeddings
POST /api/documents/reprocess
```

### Configuration Updates

Add to `appsettings.json`:
```json
{
  "RAG": {
    "MaxLocalModelTokens": 2000,
    "UseVectorDB": true,
    "VectorSimilarityThreshold": 0.7
  }
}
```

### Code Updates

No breaking changes to existing API. New fields are added to QueryResult:
- `ReferencedSources` (new)
- `UsedAdvancedModel` (new)
- `ModelUsed` (new)
- `TokenCount` (new)
- `DelegationReason` (new, nullable)

## Performance Considerations

### Vector Index Tuning

For large datasets, tune the ivfflat index:
```sql
-- More lists = faster queries, slower inserts
CREATE INDEX ... WITH (lists = 1000); -- For > 1M chunks

-- Fewer lists = slower queries, faster inserts  
CREATE INDEX ... WITH (lists = 100);  -- For < 100K chunks
```

### Group Filtering

Per-group indexing is most efficient when:
- Groups are relatively balanced in size
- Queries typically target specific groups
- Group membership doesn't change frequently

### Token Estimation

Token estimation uses `ApproximateTokensPerChar = 0.25`:
- Actual: ~0.25-0.30 tokens/char for English
- Adjust if your content differs significantly

## Troubleshooting

### Vector search returns no results

1. Check if pgvector extension is enabled:
```sql
SELECT * FROM pg_extension WHERE extname = 'vector';
```

2. Verify embeddings exist:
```sql
SELECT COUNT(*) FROM "DocumentChunks" WHERE "EmbeddingVector" IS NOT NULL;
```

3. Check fallback behavior in logs:
```
WARN: Vector DB search failed, falling back to in-memory
```

### Auto-delegation not working

1. Check configuration:
```json
"RAG": {
  "MaxLocalModelTokens": 2000  // Must be < MaxContextTokens
}
```

2. Check AI interaction logs:
```http
GET /api/ai-interactions/stats
```

### High costs from advanced model

1. Review delegation reasons:
```http
GET /api/ai-interactions/stats
```

2. Adjust threshold higher to use local model more:
```json
"MaxLocalModelTokens": 3000  // Increased from 2000
```

3. Optimize context building to reduce tokens:
```json
"ChunkSize": 400,      // Reduced from 500
"TopKResults": 3       // Reduced from 5
```

## Best Practices

1. **Enable vector DB** for > 1000 document chunks
2. **Use per-group indexing** when you have multiple isolated contexts
3. **Set MaxLocalModelTokens** based on your model's context window
4. **Monitor AI interaction logs** weekly for cost optimization
5. **Review referenced sources** to validate AI accuracy
6. **Tune vector index** based on your read/write ratio

## Future Enhancements

Planned improvements:
- [ ] Async vector index creation for large batches
- [ ] Automatic reindexing when documents move between groups
- [ ] ML-based token estimation for higher accuracy
- [ ] Cost prediction before delegation
- [ ] Source highlighting in original documents
- [ ] Vector DB health monitoring dashboard
