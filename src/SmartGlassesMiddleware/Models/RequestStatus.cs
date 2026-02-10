namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents the status of a request being monitored
/// </summary>
public class RequestStatus
{
    public Guid Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Details { get; set; }
    
    /// <summary>
    /// Progress percentage (0-100) for UI progress bar
    /// </summary>
    public int? Progress { get; set; }
    
    public DateTime StartTime { get; set; }
    public DateTime? LastUpdateTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }
}
