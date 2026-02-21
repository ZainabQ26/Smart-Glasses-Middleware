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
    private readonly ITranscriptionService _transcriptionService;
    private readonly IHistoryService _historyService;
    private readonly ILogger<AudioController> _logger;
    private readonly IRequestMonitoringService? _monitoringService;

        public AudioController(
        ITranscriptionService transcriptionService,
        IHistoryService historyService,
        ILogger<AudioController> logger,
        IRequestMonitoringService? monitoringService = null)
    {
        _transcriptionService = transcriptionService;
        _historyService = historyService;
        _logger = logger;
        _monitoringService = monitoringService;
    }

    /// <summary>
    /// Transcribes audio from base64-encoded data
    /// </summary>
    /// <param name="request">Audio request containing base64-encoded audio data</param>
    /// <returns>Transcription result with request ID</returns>
    /// <response code="200">Audio successfully transcribed</response>
    /// <response code="400">Invalid audio data or request format</response>
    /// <response code="500">Internal server error during processing</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ProcessAudio([FromBody] AudioRequest request)
    {
        string? monitoringRequestId = null;
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

            // Start monitoring
            if (_monitoringService != null)
            {
                monitoringRequestId = await _monitoringService.StartRequest("Audio", "Audio transcription request");
                await _monitoringService.UpdateRequestStatus(monitoringRequestId, "Processing", "Validating audio data", 10);
            }

            // Validate base64 format
            byte[] audioBytes;
            try
            {
                audioBytes = Convert.FromBase64String(request.AudioBase64);

                if (audioBytes.Length == 0)
                {
                    if (monitoringRequestId != null && _monitoringService != null)
                    {
                        await _monitoringService.CompleteRequest(monitoringRequestId, false, error: "Audio data is empty");
                    }
                    
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
                if (monitoringRequestId != null && _monitoringService != null)
                {
                    await _monitoringService.CompleteRequest(monitoringRequestId, false, error: "Invalid base64 format");
                }
                
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

            // Transcribe directly from memory stream (DO NOT STORE AUDIO FILE)
            string? transcription = null;
            try
            {
                if (_monitoringService != null && monitoringRequestId != null)
                {
                    await _monitoringService.UpdateRequestStatus(monitoringRequestId, "Processing", "Transcribing audio", 30);
                }
                
                using var audioStream = new MemoryStream(audioBytes);
                var fileName = $"audio_{DateTime.UtcNow:yyyyMMddHHmmss}";
                var mimeType = request.MimeType ?? "audio/wav";
                transcription = await _transcriptionService.TranscribeAsync(audioStream, fileName, mimeType);
                
                if (_monitoringService != null && monitoringRequestId != null)
                {
                    await _monitoringService.UpdateRequestStatus(monitoringRequestId, "Processing", "Transcription completed", 70);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transcription failed");
                if (monitoringRequestId != null && _monitoringService != null)
                {
                    await _monitoringService.CompleteRequest(monitoringRequestId, false, error: "Transcription failed");
                }
                throw;
            }

            // Save only the transcription text to history (no audio file storage)
            try
            {
                if (_monitoringService != null && monitoringRequestId != null)
                {
                    await _monitoringService.UpdateRequestStatus(monitoringRequestId, "Processing", "Saving transcription", 90);
                }
                
                var history = new RequestHistory
                {
                    RequestType = "AudioTranscription",
                    InputText = transcription ?? string.Empty,
                    MediaStorageKey = null // No audio file stored
                };
                if (!string.IsNullOrWhiteSpace(monitoringRequestId)) history.CorrelationId = monitoringRequestId;
                history = await _historyService.SaveRequestAsync(history);
                
                if (!string.IsNullOrWhiteSpace(monitoringRequestId) && _monitoringService != null)
                {
                    var preview = !string.IsNullOrEmpty(transcription) && transcription.Length > 50 
                        ? transcription.Substring(0, 50) + "..."
                        : transcription ?? "";
                    await _monitoringService.CompleteRequest(monitoringRequestId, true, result: $"Transcribed: {preview}");
                }

                return Ok(new { 
                    transcription, 
                    requestId = history.Id,
                    monitoringRequestId,
                    message = "Audio transcribed successfully. Audio file was not stored, only the transcription text."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save transcription history");
                return Ok(new { transcription, message = "Transcription succeeded but history save failed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio request");

            if (monitoringRequestId != null && _monitoringService != null)
            {
                await _monitoringService.CompleteRequest(monitoringRequestId, false, error: ex.Message);
            }

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
