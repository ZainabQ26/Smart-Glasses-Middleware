using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Handles media processing requests including images, videos, and audio files
/// </summary>
[ApiController]
[Route("api/ask")]
public class MediaController : ControllerBase
{
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ILogger<MediaController> _logger;
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IMediaProcessor _mediaProcessor;

    public MediaController(
        IMediaStorageService mediaStorageService,
        ITranscriptionService transcriptionService,
        IAIService aiService,
        IHistoryService historyService,
        IBackgroundTaskQueue backgroundQueue,
        IMediaProcessor mediaProcessor,
        ILogger<MediaController> logger)
    {
        _mediaStorageService = mediaStorageService;
        _transcriptionService = transcriptionService;
        _aiService = aiService;
        _historyService = historyService;
        _backgroundQueue = backgroundQueue;
        _mediaProcessor = mediaProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Process media files (images, videos, audio) with optional AI prompt
    /// </summary>
    /// <param name="request">Media request containing files and optional prompt</param>
    /// <returns>Accepted response with task ID for tracking processing status</returns>
    /// <response code="202">Request accepted for background processing</response>
    /// <response code="400">Invalid request format or unsupported file types</response>
    /// <response code="500">Internal server error during processing</response>
    /// <remarks>
    /// This endpoint accepts multipart/form-data with:
    /// - files: One or more media files (images, videos, audio)
    /// - prompt: Optional text prompt for AI processing
    /// - promptAudio: Optional audio file containing the prompt (mutually exclusive with prompt)
    /// - manifest: Optional JSON array specifying file processing order
    /// - perFileTranscriptions: Optional JSON object mapping filenames to transcriptions
    /// 
    /// Supported file formats:
    /// - Images: .jpg, .jpeg, .png, .gif
    /// - Videos: .mp4, .avi, .mov
    /// - Audio: .wav, .mp3, .m4a, .flac, .aac
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ProcessMedia([FromForm] MediaRequest request)
    {
        try
        {

            string? promptAudioTranscription = null;
            // Aggregate files from the strongly-typed model or raw form parts (guard form access)
            var files = new List<IFormFile>();
            if (request.Files?.Any() == true)
            {
                files.AddRange(request.Files!);
            }
            else if (Request.HasFormContentType)
            {
                var formFiles = Request.Form?.Files;
                if (formFiles != null && formFiles.Count > 0)
                {
                    files.AddRange(formFiles.ToList());
                }
            }

            // If a prompt audio file was uploaded as `promptAudio`, enforce exclusivity with text prompt, transcribe it, and remove it from uploads
            if (Request.HasFormContentType)
            {
                var formFiles = Request.Form?.Files;
                var promptAudioFile = formFiles?.FirstOrDefault(f => string.Equals(f.Name, "promptAudio", StringComparison.OrdinalIgnoreCase));
                if (promptAudioFile != null)
                {
                    // If both a text prompt and promptAudio are provided, reject the request
                    if (!string.IsNullOrWhiteSpace(request.Prompt))
                    {
                        var errors = new Dictionary<string, string[]>
                        {
                            ["prompt"] = new[] { "Mutually exclusive with promptAudio" }
                        };

                        var pd = new ValidationProblemDetails(errors)
                        {
                            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                            Title = "Validation Failed",
                            Status = StatusCodes.Status400BadRequest,
                            Detail = "Provide either 'prompt' (text) or 'promptAudio' (file), not both.",
                            Instance = HttpContext?.Request?.Path
                        };

                        pd.Extensions["requestId"] = HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
                        pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");

                        return BadRequest(pd);
                    }

                    try
                    {
                        using var promptStream = promptAudioFile.OpenReadStream();
                        var contentType = promptAudioFile.ContentType ?? string.Empty;
                        var transcribedPrompt = await _transcriptionService.TranscribeAsync(promptStream, promptAudioFile.FileName, contentType);
                        if (!string.IsNullOrWhiteSpace(transcribedPrompt)) promptAudioTranscription = transcribedPrompt;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to transcribe prompt audio");
                    }

                    // Ensure promptAudio is not treated as a normal media file to upload
                    files.RemoveAll(f => string.Equals(f.Name, "promptAudio", StringComparison.OrdinalIgnoreCase));
                }
            }

            // Support a single combined `input` form field which may be either a file part or a plain transcription string.
            string? inputText = null;
            if (Request.HasFormContentType)
            {
                var form = Request.Form;
                if (form != null && form.TryGetValue("input", out var iv))
                {
                    inputText = iv.FirstOrDefault();
                }
            }
            IFormFile? inputFile = null;
            if (Request.HasFormContentType)
            {
                var formFiles = Request.Form?.Files;
                if (formFiles != null && formFiles.Count > 0)
                {
                    inputFile = formFiles.FirstOrDefault(f => string.Equals(f?.Name ?? string.Empty, "input", StringComparison.OrdinalIgnoreCase));
                }
            }

            if (!files.Any() && inputFile != null)
            {
                files.Add(inputFile);
            }

            // If no files and an input string was provided, treat this as a transcription-only ask.
            if (!files.Any() && !string.IsNullOrWhiteSpace(inputText))
            {
                // Build prompt from provided transcription
                var transPrompt = request.Prompt ?? string.Empty;
                var transText = string.IsNullOrWhiteSpace(transPrompt)
                    ? inputText
                    : transPrompt + "\n\nTranscription: " + inputText;

                var (tTask, tResponse, tUsedAdvancedModel) = await _aiService.ProcessRequestAsync(transText);

                // Save history (no media key)
                try
                {
                    var history = new RequestHistory
                    {
                        RequestType = "Audio",
                        InputText = inputText,
                        DetectedTask = tTask,
                        Response = tResponse,
                        UsedAdvancedModel = tUsedAdvancedModel
                    };
                    history = await _historyService.SaveRequestAsync(history);
                    return Ok(new { task = tTask, response = tResponse, usedAdvancedModel = tUsedAdvancedModel, requestId = history.Id, transcription = inputText });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save history for transcription-only ask");
                    return Ok(new { task = tTask, response = tResponse, usedAdvancedModel = tUsedAdvancedModel, transcription = inputText });
                }
            }

            // Allowed extensions
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".avi", ".mov", ".wav", ".mp3", ".m4a", ".flac", ".aac" };
            var audioExtensions = new[] { ".wav", ".mp3", ".m4a", ".flac", ".aac" };

            // Parse manifest JSON if provided
            List<string>? manifestOrder = null;
            if (!string.IsNullOrWhiteSpace(request.Manifest))
            {
                try
                {
                    manifestOrder = System.Text.Json.JsonSerializer.Deserialize<List<string>>(request.Manifest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse manifest JSON");
                    manifestOrder = null;
                }
            }

            // Build ordered list according to manifest, then append any remaining files
            var orderedFiles = new List<IFormFile>();
            if (manifestOrder != null && manifestOrder.Count > 0)
            {
                // Validate manifest entries exist in uploaded files
                var uploadedNames = files.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);
                var missing = manifestOrder.Where(n => !uploadedNames.Contains(n)).ToList();
                if (missing.Count > 0)
                {
                    return BadRequest(new ProblemDetails { Title = "Bad Request", Detail = $"Manifest names not found in uploaded files: {string.Join(", ", missing)}", Status = StatusCodes.Status400BadRequest });
                }

                foreach (var name in manifestOrder)
                {
                    var f = files.FirstOrDefault(x => string.Equals(x.FileName, name, StringComparison.Ordinal));
                    if (f != null) orderedFiles.Add(f);
                }
                // append any files not included in manifest
                orderedFiles.AddRange(files.Where(f => !orderedFiles.Contains(f)));
            }
            else
            {
                orderedFiles = files;
            }

            // Parse per-file transcriptions JSON if provided
            Dictionary<string, string>? perFileTrans = null;
            if (!string.IsNullOrWhiteSpace(request.PerFileTranscriptions))
            {
                try
                {
                    perFileTrans = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(request.PerFileTranscriptions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse PerFileTranscriptions JSON");
                    perFileTrans = null;
                }
            }

            // Validate per-file transcription keys if provided
            if (perFileTrans != null)
            {
                var uploadedNames = files.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);
                var unknown = perFileTrans.Keys.Where(k => !uploadedNames.Contains(k)).ToList();
                if (unknown.Count > 0)
                {
                    return BadRequest(new ProblemDetails { Title = "Bad Request", Detail = $"PerFileTranscriptions contains keys not present in uploaded files: {string.Join(", ", unknown)}", Status = StatusCodes.Status400BadRequest });
                }
            }

            // If an inputText transcription was provided and files exist, attach it to the first file if no per-file transcription present
            if (!string.IsNullOrWhiteSpace(inputText) && orderedFiles.Count > 0)
            {
                var firstName = orderedFiles[0].FileName;
                if (perFileTrans == null) perFileTrans = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!perFileTrans.ContainsKey(firstName)) perFileTrans[firstName] = inputText!;
            }

            // Upload files now and enqueue a background task for processing
            var bgItems = new List<BackgroundMediaItem>();
            foreach (var file in orderedFiles)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    var pd = new ValidationProblemDetails
                    {
                        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                        Title = "Validation Failed",
                        Status = StatusCodes.Status400BadRequest,
                        Detail = $"File '{file.FileName}' has an unsupported file type. Allowed types: {string.Join(", ", allowedExtensions)}",
                        Instance = HttpContext.Request.Path
                    };
                    pd.Errors.Add("files", new[] { $"Invalid file type: {file.FileName}" });
                    pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
                    pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
                    
                    return BadRequest(pd);
                }

                using var uploadStream = file.OpenReadStream();
                var contentType = file.ContentType ?? "application/octet-stream";
                var storageKey = await _mediaStorageService.UploadMediaAsync(uploadStream, file.FileName, contentType);

                var item = new BackgroundMediaItem { FileName = file.FileName, ContentType = contentType, StorageKey = storageKey };
                // If per-file transcription provided, attach it
                if (perFileTrans != null && perFileTrans.TryGetValue(file.FileName, out var mapped))
                {
                    item.Transcription = mapped;
                }
                bgItems.Add(item);
            }

            // If an inputText transcription was provided and files exist, attach it to the first file if no per-file transcription present
            if (!string.IsNullOrWhiteSpace(inputText) && bgItems.Count > 0)
            {
                var first = bgItems[0];
                if (string.IsNullOrWhiteSpace(first.Transcription)) first.Transcription = inputText;
            }

            // Determine prompt text (if promptAudio was transcribed earlier, use it)
            var promptTextFinal = !string.IsNullOrWhiteSpace(promptAudioTranscription) ? promptAudioTranscription : request.Prompt;

            var entry = new BackgroundTaskEntry
            {
                PromptText = promptTextFinal,
                PerFileTranscriptions = perFileTrans,
                MediaItems = bgItems
            };

            await _backgroundQueue.EnqueueAsync(entry);

            var location = $"/api/tasks/{entry.Id}";
            return Accepted(location, new { taskId = entry.Id, location });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing media request");
            
            var pd = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while processing your media. Please try again later.",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
            
            return StatusCode(500, pd);
        }
    }
}
