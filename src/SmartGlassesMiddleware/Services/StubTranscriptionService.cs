namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Stub transcription service for local development.
/// This is a placeholder that returns empty string for transcription.
/// To be replaced with actual transcription service (AWS Transcribe, Whisper Lambda, etc.)
/// </summary>
public class StubTranscriptionService : ITranscriptionService
{
    private readonly ILogger<StubTranscriptionService> _logger;

    public StubTranscriptionService(ILogger<StubTranscriptionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Stub implementation - returns null
    /// </summary>
    public Task<string?> TranscribeAsync(Stream audioStream, string fileName, string mimeType, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Using stub transcription service for file '{FileName}' ({MimeType}) - audio will not be transcribed. Configure a real transcription service.", fileName, mimeType);
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Stub implementation - returns empty string
    /// </summary>
    public Task<string> TranscribeAudioAsync(Stream audioStream, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Using stub transcription service - audio will not be transcribed. Configure a real transcription service.");
        return Task.FromResult("");
    }
}
