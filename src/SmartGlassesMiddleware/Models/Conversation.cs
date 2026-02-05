namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents a chat message in a conversation
/// </summary>
public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public string Role { get; set; } = string.Empty; // "user", "assistant", "system"
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; } // Which AI model generated this (for assistant messages)
    public DateTime Timestamp { get; set; }

    // Token count for budget tracking
    public int? TokenCount { get; set; }
}

/// <summary>
/// Represents a conversation thread
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Link to ResourceGroup for context
    public Guid? ResourceGroupId { get; set; }
    public ResourceGroup? ResourceGroup { get; set; }

    // Navigation property
    public List<ChatMessage> Messages { get; set; } = new();

    // Summary for long conversations
    public string? Summary { get; set; }
    public string? SummaryEmbedding { get; set; } // JSON serialized embedding vector
    public int? SummaryEmbeddingDimension { get; set; }
}
