namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Interface for audio transcription services
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribe audio from a stream
    /// </summary>
    /// <param name="audioStream">The audio stream to transcribe</param>
    /// <param name="fileName">The file name for reference</param>
    /// <param name="mimeType">The MIME type of the audio file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transcribed text or null</returns>
    Task<string?> TranscribeAsync(Stream audioStream, string fileName, string mimeType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribe audio from a stream (alternative method signature)
    /// </summary>
    /// <param name="audioStream">The audio stream to transcribe</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transcribed text or empty string</returns>
    Task<string> TranscribeAudioAsync(Stream audioStream, CancellationToken cancellationToken = default);
}
