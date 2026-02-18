using System;

namespace SmartGlassesMiddleware.Models;

public class TranscriptionJob
{
    public Guid Id { get; set; }
    public string JobId { get; set; } = string.Empty;
    public Guid? ManagedFileId { get; set; }
    public string S3Bucket { get; set; } = string.Empty;
    public string S3Key { get; set; } = string.Empty;
    public string Status { get; set; } = "queued";
    public string? TranscriptStorageKey { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
