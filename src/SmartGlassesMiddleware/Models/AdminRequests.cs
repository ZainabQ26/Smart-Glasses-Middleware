using Microsoft.AspNetCore.Http;

namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Request model for uploading a file
/// </summary>
public class FileUploadRequest
{
    public IFormFile? File { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Request model confirming a presigned S3 upload was completed by the browser.
/// </summary>
public class CompleteUploadRequest
{
    public Guid? FileId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? Label { get; set; }
    public long FileSize { get; set; }
}

/// <summary>
/// Request model for creating or updating a prompt
/// </summary>
public class PromptRequest
{
    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
}

/// <summary>
/// Request model for creating or updating a resource group
/// </summary>
public class GroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Request model for updating server-side prevent-sleep behavior.
/// </summary>
public class PreventSleepSettingRequest
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Request model for toggling Groq command transcription at runtime.
/// </summary>
public class GroqCommandTranscriptionSettingRequest
{
    public bool Enabled { get; set; }
}
