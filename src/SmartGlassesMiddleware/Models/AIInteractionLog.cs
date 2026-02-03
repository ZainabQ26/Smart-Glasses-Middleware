namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Logs interactions with advanced AI models for auditing and analysis
/// </summary>
public class AIInteractionLog
{
    public Guid Id { get; set; }
    
    // What was sent to the AI
    public string Input { get; set; } = string.Empty;
    
    // What the AI returned
    public string Output { get; set; } = string.Empty;
    
    // Which model was used
    public string ModelUsed { get; set; } = string.Empty;
    
    // Reason for using advanced model (e.g., "token_threshold", "unknown_task", "manual")
    public string DelegationReason { get; set; } = string.Empty;
    
    // Estimated token counts
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    
    // Cost estimation (if available)
    public decimal? EstimatedCost { get; set; }
    
    // Timing
    public DateTime Timestamp { get; set; }
    public int ResponseTimeMs { get; set; }
    
    // Optional context linking
    public Guid? ResourceGroupId { get; set; }
    public ResourceGroup? ResourceGroup { get; set; }
    
    public Guid? ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    
    // Success/error tracking
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
