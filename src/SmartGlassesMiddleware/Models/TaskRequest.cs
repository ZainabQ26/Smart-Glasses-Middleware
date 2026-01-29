using System.ComponentModel.DataAnnotations;

namespace SmartGlassesMiddleware.Models;

public class TaskRequest
{
    [Required(ErrorMessage = "Text is required")]
    [StringLength(10000, MinimumLength = 1, ErrorMessage = "Text must be between 1 and 10000 characters")]
    public string Text { get; set; } = string.Empty;
}

public class AudioRequest
{
    [Required(ErrorMessage = "Audio data is required")]
    [MinLength(1, ErrorMessage = "Audio data cannot be empty")]
    public string AudioBase64 { get; set; } = string.Empty;
    
    [StringLength(10000, ErrorMessage = "Transcription must not exceed 10000 characters")]
    public string? Transcription { get; set; }
}

public class MediaRequest
{
    // Accept multiple files in a single upload. Clients should send file parts named `files`.
    public List<IFormFile>? Files { get; set; }

    // Optional overall prompt for the AI
    [StringLength(5000, ErrorMessage = "Prompt must not exceed 5000 characters")]
    public string? Prompt { get; set; }

    // Optional overall transcription (if provided by client); otherwise server will transcribe audio files.
    [StringLength(10000, ErrorMessage = "Transcription must not exceed 10000 characters")]
    public string? Transcription { get; set; }

    // Optional manifest JSON: an array of filenames (strings) indicating desired processing order.
    // Example: ["audio.wav","image.jpg"]
    public string? Manifest { get; set; }
    // Optional per-file transcriptions as JSON object mapping filename -> transcription.
    // Example: { "recording.wav": "Call Alice at 4 PM", "image.jpg": "Photo of a dog" }
    public string? PerFileTranscriptions { get; set; }
}

public class TaskResponse
{
    public string Task { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public bool UsedAdvancedModel { get; set; }
    public Guid RequestId { get; set; }
}
