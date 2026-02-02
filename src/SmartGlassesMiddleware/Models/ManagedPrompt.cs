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
    
    // Optional file attachment
    public Guid? FileId { get; set; }
    public string? FileName { get; set; }
    public string? StorageKey { get; set; }
    public string? ContentType { get; set; }
    
    // Navigation property for groups
    public List<ManagedPromptGroup> PromptGroups { get; set; } = new();
}
