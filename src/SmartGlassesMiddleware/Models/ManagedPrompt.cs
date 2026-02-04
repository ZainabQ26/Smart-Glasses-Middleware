namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents a prompt that can be used with the middleware
/// </summary>
public class ManagedPrompt
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsPinned { get; set; } = false; // Pinned prompts are prioritized in context
    
    // Navigation property for groups
    public List<ManagedPromptGroup> PromptGroups { get; set; } = new();
}
