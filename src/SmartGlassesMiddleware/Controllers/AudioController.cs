using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Handles audio transcription requests
/// </summary>
[ApiController]
[Route("api/transcribe")]
public class AudioController : ControllerBase
{
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IHistoryService _historyService;
    private readonly IRagService _ragService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AudioController> _logger;

    public AudioController(
        IMediaStorageService mediaStorageService,
        ITranscriptionService transcriptionService,
        IHistoryService historyService,
        IRagService ragService,
        ApplicationDbContext context,
        ILogger<AudioController> logger)
    {
        _mediaStorageService = mediaStorageService;
        _transcriptionService = transcriptionService;
        _historyService = historyService;
        _ragService = ragService;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Transcribes audio from base64-encoded data
    /// </summary>
    /// <param name="request">Audio request containing base64-encoded audio data</param>
    /// <returns>Transcription result with storage key and request ID</returns>
    /// <response code="200">Audio successfully transcribed</response>
    /// <response code="400">Invalid audio data or request format</response>
    /// <response code="500">Internal server error during processing</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ProcessAudio([FromBody] AudioRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.AudioBase64))
            {
                var pd = new ValidationProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = "Validation Failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Audio data is required and cannot be empty.",
                    Instance = HttpContext.Request.Path
                };
                pd.Errors.Add("audioBase64", new[] { "Audio data cannot be empty" });
                pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
                pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
                
                return BadRequest(pd);
            }

            // Validate base64 format
            byte[] audioBytes;
            try
            {
                audioBytes = Convert.FromBase64String(request.AudioBase64);
                
                if (audioBytes.Length == 0)
                {
                    var pd = new ValidationProblemDetails
                    {
                        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                        Title = "Validation Failed",
                        Status = StatusCodes.Status400BadRequest,
                        Detail = "Audio data cannot be empty.",
                        Instance = HttpContext.Request.Path
                    };
                    pd.Errors.Add("audioBase64", new[] { "Audio data decoded to empty byte array" });
                    pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
                    pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
                    
                    return BadRequest(pd);
                }
            }
            catch (FormatException)
            {
                var pd = new ValidationProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = "Validation Failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Audio data must be valid base64-encoded string.",
                    Instance = HttpContext.Request.Path
                };
                pd.Errors.Add("audioBase64", new[] { "Invalid base64 format" });
                pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
                pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
                
                return BadRequest(pd);
            }

            // Store audio in MinIO
            using var audioStream = new MemoryStream(audioBytes);
            var fileName = $"audio_{DateTime.UtcNow:yyyyMMddHHmmss}.wav";
            var storageKey = await _mediaStorageService.UploadMediaAsync(
                audioStream,
                fileName,
                "audio/wav");

            // Download and transcribe
            string? transcription = null;
            try
            {
                using var downloaded = await _mediaStorageService.DownloadMediaAsync(storageKey);
                transcription = await _transcriptionService.TranscribeAsync(downloaded, fileName, "audio/wav");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transcription failed for {Key}", storageKey);
            }

            // Create ManagedFile record and process for RAG indexing
            try
            {
                var managedFile = new ManagedFile
                {
                    Id = Guid.NewGuid(),
                    FileName = fileName,
                    Label = $"Audio transcription: {fileName}",
                    ContentType = "audio/wav",
                    StorageKey = storageKey,
                    UploadedAt = DateTime.UtcNow,
                    FileSize = audioBytes.Length
                };
                _context.ManagedFiles.Add(managedFile);
                await _context.SaveChangesAsync();

                await _ragService.ProcessFileForRagAsync(
                    managedFile.Id,
                    fileName,
                    "audio/wav",
                    storageKey,
                    transcription);
                _logger.LogInformation("Audio file {FileName} processed for RAG with ID {FileId}", fileName, managedFile.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process audio file for RAG, but upload succeeded");
            }

            // Save transcription to history
            try
            {
                var history = new RequestHistory
                {
                    RequestType = "Audio",
                    InputText = transcription ?? string.Empty,
                    MediaStorageKey = storageKey
                };
                history = await _historyService.SaveRequestAsync(history);

                return Ok(new { storageKey, transcription, requestId = history.Id });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save history for audio {Key}", storageKey);
                return Ok(new { storageKey, transcription });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio request");
            
            var pd = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while processing your audio. Please try again later.",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
            
            return StatusCode(500, pd);
        }
    }
}
