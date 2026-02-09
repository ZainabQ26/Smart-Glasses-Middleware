namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Result of a RAG query including the answer and source documents
/// </summary>
public class QueryResult
{
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Conversation id used for this query (if any)
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>
    /// All sources that were provided to the AI as context
    /// </summary>
    public List<DocumentSource> Sources { get; set; } = new();

    /// <summary>
    /// Sources that the AI explicitly referenced in its answer
    /// </summary>
    public List<DocumentSource> ReferencedSources { get; set; } = new();

    /// <summary>
    /// Conversation messages that were used as context
    /// </summary>
    public List<ConversationSource> ConversationSources { get; set; } = new();

    /// <summary>
    /// Whether the advanced model was used for this query
    /// </summary>
    public bool UsedAdvancedModel { get; set; }

    /// <summary>
    /// Which model processed the query
    /// </summary>
    public string ModelUsed { get; set; } = string.Empty;

    /// <summary>
    /// Estimated tokens in the query context
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Reason for delegation to advanced model (if applicable)
    /// </summary>
    public string? DelegationReason { get; set; }
}

/// <summary>
/// Source document information for a query result
/// </summary>
public class DocumentSource
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public Guid ChunkId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
}

/// <summary>
/// Conversation message source information for a query result
/// </summary>
public class ConversationSource
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
