using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ILogger<MediaController> _logger;
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;

    public MediaController(
        IMediaStorageService mediaStorageService,
        ITranscriptionService transcriptionService,
        IAIService aiService,
        IHistoryService historyService,
        ILogger<MediaController> logger)
    {
        _mediaStorageService = mediaStorageService;
        _transcriptionService = transcriptionService;
        _aiService = aiService;
        _historyService = historyService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult> ProcessMedia([FromForm] MediaRequest request)
    {
        try
        {
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest("File cannot be empty");
            }

            // Validate file type (images, video, audio)
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".avi", ".mov", ".wav", ".mp3", ".m4a", ".flac", ".aac" };
            var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest("Invalid file type. Supported types: images (jpg, png, gif) and videos (mp4, avi, mov)");
            }

            // Store media in MinIO
            using var uploadStream = request.File.OpenReadStream();
            var storageKey = await _mediaStorageService.UploadMediaAsync(
                uploadStream,
                request.File.FileName,
                request.File.ContentType);

            // Known audio/video extensions
            var audioExtensions = new[] { ".wav", ".mp3", ".m4a", ".flac", ".aac" };

            string transcription = string.Empty;
            // If this is audio and no transcription provided, call the transcription service
            if (audioExtensions.Contains(extension) && string.IsNullOrWhiteSpace(request.Transcription))
            {
                try
                {
                    using var downloaded = await _mediaStorageService.DownloadMediaAsync(storageKey);
                    transcription = await _transcriptionService.TranscribeAsync(downloaded, request.File.FileName, request.File.ContentType);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Transcription failed for {Key}", storageKey);
                }
            }

            // Return storage key and transcription (if any). Do not call AI or save history here.
            // Build text for AI processing
            var prompt = request.Prompt ?? $"[{extension} file uploaded - {request.File.FileName}]";
            var transcriptionText = string.IsNullOrWhiteSpace(request.Transcription) ? (string.IsNullOrWhiteSpace(transcription) ? string.Empty : $"\nTranscription: {transcription}") : $"\nTranscription: {request.Transcription}";

            var textToProcess = $@"{prompt}{transcriptionText}

MediaStorageKey: {storageKey}
MediaFileName: {request.File.FileName}
MediaContentType: {request.File.ContentType}";

            // Process with AI
            var (task, response, usedAdvancedModel) = await _aiService.ProcessRequestAsync(textToProcess);

            // Save to history
            try
            {
                var history = new RequestHistory
                {
                    RequestType = audioExtensions.Contains(extension) ? "Audio" : (new[] { ".mp4", ".avi", ".mov" }.Contains(extension) ? "Video" : "Image"),
                    InputText = textToProcess,
                    DetectedTask = task,
                    Response = response,
                    UsedAdvancedModel = usedAdvancedModel,
                    MediaStorageKey = storageKey
                };
                history = await _historyService.SaveRequestAsync(history);

                return Ok(new { task, response, usedAdvancedModel, requestId = history.Id, transcription = string.IsNullOrWhiteSpace(request.Transcription) ? transcription : request.Transcription });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save history for media {Key}", storageKey);
                return Ok(new { task, response, usedAdvancedModel, transcription = string.IsNullOrWhiteSpace(request.Transcription) ? transcription : request.Transcription });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing media request");
            return StatusCode(500, "An error occurred processing your request");
        }
    }
}
