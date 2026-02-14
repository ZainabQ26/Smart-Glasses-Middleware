namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents metadata for a document uploaded to the RAG system
/// </summary>
public class DocumentMetadata
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public long FileSize { get; set; }
    public int TotalPages { get; set; }
    public string DocumentType { get; set; } = string.Empty; // pdf, docx, txt, image
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessingError { get; set; }

    // Navigation properties
    public List<DocumentChunk> Chunks { get; set; } = new();
    public List<DocumentGroup> DocumentGroups { get; set; } = new();
    public List<ChatMessageAttachment> MessageAttachments { get; set; } = new();
}

/// <summary>
/// Join table for many-to-many relationship between DocumentMetadata and ResourceGroup
/// </summary>
public class DocumentGroup
{
    public Guid DocumentMetadataId { get; set; }
    public DocumentMetadata DocumentMetadata { get; set; } = null!;

    public Guid ResourceGroupId { get; set; }
    public ResourceGroup ResourceGroup { get; set; } = null!;
}

/// <summary>
/// Represents a chunk of text extracted from a document with positional information
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentMetadataId { get; set; }
    public DocumentMetadata DocumentMetadata { get; set; } = null!;

    public string ChunkText { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; } // Order within the document

    // Positional information (bounding box on page)
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    // Embedding for semantic search
    public string? Embedding { get; set; } // Stored as JSON array (fallback)
    public int? EmbeddingDimension { get; set; } // Dimension of the embedding vector

    // Vector embedding for pgvector (PostgreSQL native vector type)
    public Pgvector.Vector? EmbeddingVector { get; set; }

    public DateTime CreatedAt { get; set; }
}
