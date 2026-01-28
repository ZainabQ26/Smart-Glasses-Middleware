using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioController : ControllerBase
{
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IHistoryService _historyService;
    private readonly ILogger<AudioController> _logger;

    public AudioController(
        IMediaStorageService mediaStorageService,
        ITranscriptionService transcriptionService,
        IHistoryService historyService,
        ILogger<AudioController> logger)
    {
        _mediaStorageService = mediaStorageService;
        _transcriptionService = transcriptionService;
        _historyService = historyService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult> ProcessAudio([FromBody] AudioRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.AudioBase64))
            {
                return BadRequest("Audio data cannot be empty");
            }

            // Store audio in MinIO
            var audioBytes = Convert.FromBase64String(request.AudioBase64);
            using var audioStream = new MemoryStream(audioBytes);
            var fileName = $"audio_{DateTime.UtcNow:yyyyMMddHHmmss}.wav";
            var storageKey = await _mediaStorageService.UploadMediaAsync(
                audioStream,
                fileName,
                "audio/wav");

            // Download and transcribe
            string transcription = string.Empty;
            try
            {
                using var downloaded = await _mediaStorageService.DownloadMediaAsync(storageKey);
                transcription = await _transcriptionService.TranscribeAsync(downloaded, fileName, "audio/wav");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transcription failed for {Key}", storageKey);
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
            return StatusCode(500, "An error occurred processing your request");
        }
    }
}
