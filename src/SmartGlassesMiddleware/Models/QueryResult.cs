namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Result of a RAG query including the answer and source documents
/// </summary>
public class QueryResult
{
    public string Answer { get; set; } = string.Empty;
    public List<DocumentSource> Sources { get; set; } = new();
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
