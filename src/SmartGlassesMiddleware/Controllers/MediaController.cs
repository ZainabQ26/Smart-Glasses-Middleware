using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ILogger<MediaController> _logger;

    public MediaController(
        IAIService aiService,
        IHistoryService historyService,
        IMediaStorageService mediaStorageService,
        ITranscriptionService transcriptionService,
        ILogger<MediaController> logger)
    {
        _aiService = aiService;
        _historyService = historyService;
        _mediaStorageService = mediaStorageService;
        _transcriptionService = transcriptionService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> ProcessMedia([FromForm] MediaRequest request)
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
            var videoExtensions = new[] { ".mp4", ".avi", ".mov" };
            // If this is audio and no transcription provided, call the transcription service
            if (audioExtensions.Contains(extension) && string.IsNullOrWhiteSpace(request.Transcription))
            {
                try
                {
                    using var downloaded = await _mediaStorageService.DownloadMediaAsync(storageKey);
                    var transcribed = await _transcriptionService.TranscribeAsync(downloaded, request.File.FileName, request.File.ContentType);
                    if (!string.IsNullOrWhiteSpace(transcribed)) request.Transcription = transcribed;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Transcription failed for {Key}", storageKey);
                }
            }

            // Process prompt/transcription with AI; include media metadata so AI can consider the file too
            var prompt = request.Prompt ?? $"[{extension} file uploaded - {request.File.FileName}]";
            var transcriptionText = string.IsNullOrWhiteSpace(request.Transcription) ? string.Empty : $"\nTranscription: {request.Transcription}";

            var textToProcess = $@"{prompt}{transcriptionText}

MediaStorageKey: {storageKey}
MediaFileName: {request.File.FileName}
MediaContentType: {request.File.ContentType}";

            var (task, response, usedStrongerModel) = await _aiService.ProcessRequestAsync(textToProcess);

            // Save to history

            var history = new RequestHistory
            {
                RequestType = audioExtensions.Contains(extension) ? "Audio" : (videoExtensions.Contains(extension) ? "Video" : "Image"),
                InputText = textToProcess,
                DetectedTask = task,
                Response = response,
                UsedStrongerModel = usedStrongerModel,
                MediaStorageKey = storageKey
            };
            history = await _historyService.SaveRequestAsync(history);

            return Ok(new TaskResponse
            {
                Task = task,
                Response = response,
                UsedStrongerModel = usedStrongerModel,
                RequestId = history.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing media request");
            return StatusCode(500, "An error occurred processing your request");
        }
    }
}
