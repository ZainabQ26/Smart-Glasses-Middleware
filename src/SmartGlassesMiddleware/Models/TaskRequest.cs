namespace SmartGlassesMiddleware.Models;

public class TaskRequest
{
    public string Text { get; set; } = string.Empty;
}

public class AudioRequest
{
    public string AudioBase64 { get; set; } = string.Empty;
    public string? Transcription { get; set; }
}

public class MediaRequest
{
    public IFormFile? File { get; set; }
    public string? Description { get; set; }
}

public class TaskResponse
{
    public string Task { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public bool UsedStrongerModel { get; set; }
    public Guid RequestId { get; set; }
}
