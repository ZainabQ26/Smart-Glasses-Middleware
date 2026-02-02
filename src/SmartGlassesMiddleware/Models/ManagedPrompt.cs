namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents a prompt that can be used with the middleware
/// </summary>
public class ManagedPrompt
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Optional display name for the prompt. If not provided, defaults to "Unnamed Prompt" in the UI.
    /// </summary>
    public string? Name { get; set; }
    
    public string PromptText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    
    /// <summary>
    /// Optional file attachment metadata.
    /// These fields store information about a file that can be attached to the prompt for context.
    /// </summary>
    public string? FileName { get; set; }
    
    /// <summary>
    /// Storage key for retrieving the attached file from object storage (MinIO).
    /// </summary>
    public string? StorageKey { get; set; }
    
    /// <summary>
    /// MIME content type of the attached file (e.g., "image/png", "application/pdf").
    /// </summary>
    public string? ContentType { get; set; }
    
    // Navigation property for groups
    public List<ManagedPromptGroup> PromptGroups { get; set; } = new();
}
