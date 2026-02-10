# Iterative RAG: AI-Driven Document Retrieval

## Overview

The Smart Glasses Middleware now supports **Iterative RAG (Retrieval-Augmented Generation)**, a powerful feature that allows the AI to dynamically request information from the document database as needed during a conversation.

## Traditional vs Iterative RAG

### Traditional RAG (Default)
- All relevant documents are retrieved upfront based on the user's query
- Context is included in a single prompt to the AI
- The AI answers based only on the provided context
- Simpler, faster, and more predictable

### Iterative RAG (Optional)
- The AI can make multiple queries to the document database during processing
- The AI decides what information it needs and when to request it
- Uses Semantic Kernel's function calling to enable AI-RAG interaction
- More flexible and can handle complex, multi-faceted queries
- Sources are accurately tracked across all retrieval operations

## How It Works

1. **AI Receives Query**: User submits a question through the API
2. **AI Analyzes**: The AI determines if it needs document information
3. **Function Call**: AI calls the `search_documents` function with a specific query
4. **RAG Responds**: The RAG system searches and returns relevant document chunks
5. **AI Processes**: AI reviews the information and can make additional queries if needed
6. **Final Answer**: AI provides a comprehensive answer with proper source citations
7. **Sources Tracked**: All accessed documents and chunks are tracked and returned

## Configuration

### Enable Iterative RAG

**Via appsettings.json:**
```json
{
  "RAG": {
    "UseIterativeRetrieval": true,
    "MaxIterativeQueries": 5
  }
}
```

**Via environment variables (.env file):**
```bash
RAG_USE_ITERATIVE_RETRIEVAL=true
RAG_MAX_ITERATIVE_QUERIES=5
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `UseIterativeRetrieval` | boolean | `false` | Enable iterative RAG mode |
| `MaxIterativeQueries` | integer | `5` | Maximum number of document searches the AI can make per query |

## API Usage

The iterative RAG mode is automatically used when enabled. No changes to API calls are required:

```bash
# Query documents (uses iterative mode if enabled)
curl -X POST http://localhost:5000/api/documents/query \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What are the main findings in the research?",
    "topK": 5
  }'
```

### Response Structure

The response includes all sources queried during the iterative process:

```json
{
  "answer": "Based on the documents...",
  "sources": [
    {
      "documentId": "guid",
      "fileName": "research.pdf",
      "pageNumber": 1,
      "chunkId": "guid",
      "chunkText": "..."
    }
  ],
  "referencedSources": [...],
  "usedAdvancedModel": false,
  "modelUsed": "local-iterative",
  "tokenCount": 1500,
  "delegationReason": "iterative_retrieval"
}
```

## Use Cases

### When to Use Iterative RAG

✅ **Complex queries** requiring information from multiple topics
✅ **Follow-up questions** that need additional context
✅ **Exploratory research** where the AI needs to navigate documents
✅ **Multi-step reasoning** requiring progressive information gathering

### When to Use Traditional RAG

✅ **Simple factual queries** with clear information needs
✅ **Performance-critical** applications requiring fast responses
✅ **Predictable workloads** where context size is known
✅ **Resource-constrained** environments

## Source Tracking

All document sources accessed during iterative retrieval are tracked and reported:

- **`sources`**: All document chunks provided to the AI
- **`referencedSources`**: Chunks explicitly cited in the answer
- Each source includes: document ID, file name, page number, chunk ID, and preview text

## Performance Considerations

- **Latency**: Iterative mode may be slower due to multiple AI-RAG interactions
- **Token Usage**: Can use more tokens as the AI processes retrieved information
- **Accuracy**: Potentially more accurate for complex queries as AI can refine its searches
- **Cost**: Uses the local model with function calling (not the advanced model)

## Example Scenarios

### Scenario 1: Multi-Topic Research
**Query**: "Compare the methodology sections from all uploaded research papers"

**Iterative Process**:
1. AI searches for "methodology sections in research papers"
2. Retrieves relevant chunks from multiple papers
3. AI identifies gaps, searches for "specific methodology details"
4. Combines information and provides comprehensive comparison

### Scenario 2: Clarification Seeking
**Query**: "What did the author conclude about climate change?"

**Iterative Process**:
1. AI searches for "author conclusions climate change"
2. Finds general conclusion
3. AI searches for "supporting evidence for climate conclusions"
4. Provides detailed answer with supporting context

## Troubleshooting

### Iterative Mode Not Working

1. **Check Configuration**: Ensure `UseIterativeRetrieval` is set to `true`
2. **Verify Model Support**: Confirm Ollama model supports function calling
3. **Review Logs**: Check application logs for function call attempts
4. **Test Simple Query**: Try a straightforward question to verify setup

### Too Many Iterations

If hitting the `MaxIterativeQueries` limit frequently:
- Increase the limit in configuration
- Ensure documents are properly indexed
- Consider using traditional RAG for simpler queries

### Sources Not Tracked

All sources accessed via iterative retrieval are automatically tracked. If sources are missing:
- Check that documents have proper metadata
- Verify embeddings are generated
- Review query logs for retrieval failures

## Technical Details

### Implementation

The iterative RAG system uses:
- **Semantic Kernel**: For AI orchestration and function calling
- **RAGPlugin**: Custom plugin exposing `search_documents` function
- **Auto-invocation**: Automatic function execution via `ToolCallBehavior.AutoInvokeKernelFunctions`
- **Source Aggregation**: Collects all accessed sources across iterations

### Security

- Function calls are executed server-side only
- Document access respects resource group permissions
- Query limits prevent infinite loops
- All interactions are logged for auditing

## Migration Guide

### Enabling on Existing Deployment

1. Update configuration file or environment variables
2. Restart the application
3. No database migrations required
4. Existing queries continue to work

### Disabling Iterative Mode

Set `UseIterativeRetrieval=false` and restart. The system reverts to traditional RAG mode.

## Best Practices

1. **Start with Traditional**: Use traditional RAG first, enable iterative only if needed
2. **Monitor Performance**: Track query latency and token usage
3. **Set Appropriate Limits**: Configure `MaxIterativeQueries` based on your use case
4. **Document Quality**: Ensure documents are well-structured for better retrieval
5. **Test Thoroughly**: Validate behavior with representative queries

## Future Enhancements

Potential improvements to iterative RAG:
- Per-conversation mode selection
- Adaptive iteration limits based on query complexity
- Hybrid mode combining upfront and iterative retrieval
- Advanced source relevance scoring
- Conversation-aware iterative retrieval
