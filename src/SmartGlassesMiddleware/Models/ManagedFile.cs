namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents a file uploaded and managed in the database
/// </summary>
public class ManagedFile
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public DateTime UploadedAt { get; set; }
    public long FileSize { get; set; }

    // Navigation property for groups
    public List<ManagedFileGroup> FileGroups { get; set; } = new();

    // Navigation property for message attachments
    public List<ChatMessageFile> MessageFiles { get; set; } = new();
}
