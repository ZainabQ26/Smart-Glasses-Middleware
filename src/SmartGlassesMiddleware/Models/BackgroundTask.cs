using System.Collections.Concurrent;

namespace SmartGlassesMiddleware.Models;

public enum BackgroundTaskStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public class BackgroundMediaItem
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? Transcription { get; set; }
}

public class BackgroundTaskEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public BackgroundTaskStatus Status { get; set; } = BackgroundTaskStatus.Queued;
    // Optional monitoring request id tracked by the RequestMonitoringService / SignalR
    public string? MonitoringRequestId { get; set; }
    public string? PromptText { get; set; }
    public Dictionary<string, string>? PerFileTranscriptions { get; set; }
    public List<BackgroundMediaItem> MediaItems { get; set; } = new List<BackgroundMediaItem>();
    public object? Result { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
