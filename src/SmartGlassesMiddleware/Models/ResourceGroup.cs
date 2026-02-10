using System.Text.Json.Serialization;

namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents a group that can contain files and prompts
/// </summary>
public class ResourceGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public List<ManagedFileGroup> FileGroups { get; set; } = new();
    public List<ManagedPromptGroup> PromptGroups { get; set; } = new();
    public List<DocumentGroup> DocumentGroups { get; set; } = new();
    [JsonIgnore]
    public List<Conversation> Conversations { get; set; } = new();
}

/// <summary>
/// Join table for many-to-many relationship between ManagedFile and ResourceGroup
/// </summary>
public class ManagedFileGroup
{
    public Guid ManagedFileId { get; set; }
    public ManagedFile ManagedFile { get; set; } = null!;

    public Guid ResourceGroupId { get; set; }
    public ResourceGroup ResourceGroup { get; set; } = null!;
}

/// <summary>
/// Join table for many-to-many relationship between ManagedPrompt and ResourceGroup
/// </summary>
public class ManagedPromptGroup
{
    public Guid ManagedPromptId { get; set; }
    public ManagedPrompt ManagedPrompt { get; set; } = null!;

    public Guid ResourceGroupId { get; set; }
    public ResourceGroup ResourceGroup { get; set; } = null!;
}
