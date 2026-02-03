namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents a file entry in the RAG (Retrieval-Augmented Generation) index
/// </summary>
public class RagEntry
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? Transcription { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string IndexedContent { get; set; } = string.Empty;
    public float[]? EmbeddingVector { get; set; }
}
