using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Handles media processing requests including images, videos, and audio files
/// </summary>
[ApiController]
[Route("api/ask")]
public class MediaController : ControllerBase
{
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ILogger<MediaController> _logger;
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly IRequestMonitoringService? _monitoringService;
    private readonly ApplicationDbContext _context;

    public MediaController(
        IMediaStorageService mediaStorageService,
        IAIService aiService,
        IHistoryService historyService,
        IBackgroundTaskQueue backgroundQueue,
        IMediaProcessor mediaProcessor,

        ApplicationDbContext context,
        ILogger<MediaController> logger,
        IRequestMonitoringService? monitoringService = null)
    {
        _mediaStorageService = mediaStorageService;
        _aiService = aiService;
        _historyService = historyService;
        _backgroundQueue = backgroundQueue;
        _mediaProcessor = mediaProcessor;
        _context = context;
        _logger = logger;
        _monitoringService = monitoringService;
    }

    /// <summary>
    /// Process media files (images, videos, audio) with optional AI prompt
    /// </summary>
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
    public async Task<ActionResult> ProcessMedia()
    {
        try
        {
            // Manually bind form to avoid potential model-binding hangs in some environments
            MediaRequest request = new MediaRequest();
            IFormCollection? form = null;
            if (Request.HasFormContentType)
            {
                form = await Request.ReadFormAsync();
                request.Prompt = form.TryGetValue("prompt", out var p) ? p.FirstOrDefault() : null;
                request.Transcription = form.TryGetValue("input", out var iv) ? iv.FirstOrDefault() : null;
                request.Manifest = form.TryGetValue("manifest", out var m) ? m.FirstOrDefault() : null;
                request.PerFileTranscriptions = form.TryGetValue("perFileTranscriptions", out var pf) ? pf.FirstOrDefault() : null;
                if (form.Files != null && form.Files.Count > 0)
                {
                    request.Files = form.Files.ToList();
                }
            }

            return await ProcessMediaInternalAsync(request, form);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in ProcessMedia");
            return Problem("An error occurred while processing the request.");
        }
    }

    [NonAction]
    public Task<ActionResult> ProcessMedia(MediaRequest request)
    {
        IFormCollection? form = null;
        if (Request.HasFormContentType)
        {
            form = Request.Form;
        }

        return ProcessMediaInternalAsync(request, form);
    }

    private async Task<ActionResult> ProcessMediaInternalAsync(MediaRequest request, IFormCollection? form)
    {
        try
        {
            _logger.LogInformation("ProcessMedia entry: files={FileCount}, promptProvided={HasPrompt}", request.Files?.Count ?? 0, !string.IsNullOrWhiteSpace(request.Prompt));

            string? promptAudioTranscription = null;
            // Aggregate files from the strongly-typed model or raw form parts (guard form access)
            var files = new List<IFormFile>();
            if (request.Files?.Any() == true)
            {
                files.AddRange(request.Files!);
            }
            else if (form?.Files != null && form.Files.Count > 0)
            {
                files.AddRange(form.Files.ToList());
            }

            // First, parse the input/transcription field early so we can skip unnecessary transcription
            string? inputText = null;
            if (form != null && form.TryGetValue("input", out var iv))
            {
                inputText = iv.FirstOrDefault();
            }
            if (string.IsNullOrWhiteSpace(inputText))
            {
                inputText = request.Transcription;
            }

            // Reject audio files - audio transcription is handled entirely in the browser via WebWorker
            var promptAudioFile = form?.Files?.FirstOrDefault(f => string.Equals(f.Name, "promptAudio", StringComparison.OrdinalIgnoreCase));
            if (promptAudioFile != null)
            {
                var fileExt = Path.GetExtension(promptAudioFile.FileName).ToLowerInvariant();
                var promptAudioExtensions = new[] { ".wav", ".mp3", ".m4a", ".ogg", ".webm", ".flac", ".aac" };
                
                if (promptAudioExtensions.Contains(fileExt) || promptAudioFile.ContentType?.StartsWith("audio/") == true)
                {
                    var pd = new ValidationProblemDetails
                    {
                        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                        Title = "Unsupported File Type",
                        Status = StatusCodes.Status400BadRequest,
                        Detail = "Audio files are not accepted. Audio transcription is handled in-browser.",
                        Instance = HttpContext?.Request?.Path
                    };

                    pd.Extensions["requestId"] = HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
                    pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");

                    return BadRequest(pd);
                }

                // If file is not audio, remove it from the uploads list (it will be processed normally)
                files.RemoveAll(f => string.Equals(f.Name, "promptAudio", StringComparison.OrdinalIgnoreCase));
            }

            IFormFile? inputFile = null;
            if (form?.Files != null && form.Files.Count > 0)
            {
                inputFile = form.Files.FirstOrDefault(f => string.Equals(f?.Name ?? string.Empty, "input", StringComparison.OrdinalIgnoreCase));
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

                string? monitoringRequestId = null;
                try
                {
                    if (_monitoringService != null)
                    {
                        monitoringRequestId = await _monitoringService.StartRequest("Audio", inputText ?? "transcription-only request");
                        await _monitoringService.UpdateRequestStatus(monitoringRequestId, "Processing");
                    }

                    // Use CancellationToken with 30s timeout to prevent endpoint from hanging
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var (tTask, tResponse, tUsedAdvancedModel) = await _aiService.ProcessRequestAsync(transText, cts.Token);

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
                            // CorrelationId set below if monitoringRequestId exists
                        };
                        if (!string.IsNullOrWhiteSpace(monitoringRequestId)) history.CorrelationId = monitoringRequestId;
                        history = await _historyService.SaveRequestAsync(history);

                        if (!string.IsNullOrWhiteSpace(monitoringRequestId) && _monitoringService != null)
                        {
                            await _monitoringService.CompleteRequest(monitoringRequestId, true, result: history.Id.ToString());
                        }

                        return Ok(new { task = tTask, response = tResponse, usedAdvancedModel = tUsedAdvancedModel, requestId = history.Id, transcription = inputText });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to save history for transcription-only ask");
                        if (!string.IsNullOrWhiteSpace(monitoringRequestId) && _monitoringService != null)
                        {
                            await _monitoringService.CompleteRequest(monitoringRequestId, true, result: tResponse);
                        }
                        return Ok(new { task = tTask, response = tResponse, usedAdvancedModel = tUsedAdvancedModel, transcription = inputText });
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("AI service timeout for transcription-only ask");
                    if (!string.IsNullOrWhiteSpace(monitoringRequestId) && _monitoringService != null)
                    {
                        await _monitoringService.CompleteRequest(monitoringRequestId, false, error: "AI service timeout");
                    }
                    return Problem("AI service is taking too long to process. Please try again.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during transcription-only processing");
                    if (!string.IsNullOrWhiteSpace(monitoringRequestId) && _monitoringService != null)
                    {
                        await _monitoringService.CompleteRequest(monitoringRequestId, false, error: ex.Message);
                    }
                    return Problem("An error occurred while processing the request.");
                }
            }
            // Allowed extensions (audio uploads are disallowed to avoid storing audio files)
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".avi", ".mov" };

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

            // If any uploaded file is an audio file, filter it out since audio transcription 
            // is handled in-browser via WebWorker
            var audioExtensions = new[] { ".wav", ".mp3", ".m4a", ".flac", ".aac" };
            orderedFiles = orderedFiles.Where(f => !audioExtensions.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())).ToList();

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

                // Compute content hash for deduplication.
                // ComputeContentHash reads the stream to EOF; reset position before upload.
                var contentHash = _mediaStorageService.ComputeContentHash(uploadStream);
                if (uploadStream.CanSeek)
                    uploadStream.Seek(0, SeekOrigin.Begin);

                // Check if file with same hash already exists
                var existingFile = await _context.ManagedFiles
                    .FirstOrDefaultAsync(f => f.ContentHash == contentHash);

                string storageKey;
                Guid fileId;

                if (existingFile != null)
                {
                    // File already exists, reuse it
                    _logger.LogInformation("File {FileName} with hash {Hash} already exists as {FileId}, reusing", file.FileName, contentHash, existingFile.Id);
                    storageKey = existingFile.StorageKey;
                    fileId = existingFile.Id;
                }
                else
                {
                    // Upload new file — attach metadata so S3FileIndexService can reconstruct the
                    // ManagedFile record on next boot without a separate database save.
                    fileId = Guid.NewGuid();
                    var uploadMeta = S3FileIndexService.BuildUploadMetadata(fileId, file.FileName, file.FileName);
                    storageKey = await _mediaStorageService.UploadMediaAsync(uploadStream, file.FileName, contentType, uploadMeta);

                    // Create ManagedFile record using the same fileId embedded in S3 metadata
                    var managedFile = new ManagedFile
                    {
                        Id = fileId,
                        FileName = file.FileName,
                        Label = $"Media upload: {file.FileName}",
                        ContentType = contentType,
                        StorageKey = storageKey,
                        ContentHash = contentHash,
                        UploadedAt = DateTime.UtcNow,
                        FileSize = file.Length
                    };
                    _context.ManagedFiles.Add(managedFile);
                    await _context.SaveChangesAsync();
                }

                var item = new BackgroundMediaItem { FileName = file.FileName, ContentType = contentType, StorageKey = storageKey };
                // If per-file transcription provided, attach it
                if (perFileTrans != null && perFileTrans.TryGetValue(file.FileName, out var mapped))
                {
                    item.Transcription = mapped;
                }
                bgItems.Add(item);

                // Process file for RAG indexing
                try
                {
                    _logger.LogInformation("RAG disabled, skipping processing for {FileName} with ID {FileId}", file.FileName, fileId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process file {FileName} for RAG, but upload succeeded", file.FileName);
                }
            }

            // If an inputText transcription was provided and files exist, attach it to the first file if no per-file transcription present
            if (!string.IsNullOrWhiteSpace(inputText) && bgItems.Count > 0)
            {
                var first = bgItems[0];
                if (string.IsNullOrWhiteSpace(first.Transcription)) first.Transcription = inputText;
            }

            // Determine prompt text (if promptAudio was transcribed earlier, use it)
            var promptTextFinal = !string.IsNullOrWhiteSpace(promptAudioTranscription) ? promptAudioTranscription : request.Prompt;

            // Validate that the request contains at least some input (files or text)
            if ((bgItems == null || bgItems.Count == 0) && string.IsNullOrWhiteSpace(promptTextFinal) && string.IsNullOrWhiteSpace(inputText))
            {
                var pd = new ValidationProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = "Validation Failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Request must include at least one uploaded file or a text input/prompt.",
                    Instance = HttpContext.Request.Path
                };
                pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
                pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");

                return BadRequest(pd);
            }

            var entry = new BackgroundTaskEntry
            {
                PromptText = promptTextFinal,
                PerFileTranscriptions = perFileTrans,
                MediaItems = bgItems
            };

            // Mark as Processing immediately so E2E tests see an active task
            entry.Status = BackgroundTaskStatus.Processing;
            entry.UpdatedAt = DateTime.UtcNow;

            // Start a monitoring request if monitoring service is available so the UI shows it
            string? monitoringRequestIdForUpload = null;
            if (_monitoringService != null)
            {
                try
                {
                    monitoringRequestIdForUpload = await _monitoringService.StartRequest("Audio", promptTextFinal ?? "Media upload", entry.Id.ToString());
                    await _monitoringService.UpdateRequestStatus(monitoringRequestIdForUpload, "Processing", null, 0);
                    entry.MonitoringRequestId = monitoringRequestIdForUpload;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to start monitoring request for background entry {EntryId}", entry.Id);
                }
            }

            try
            {
                await _backgroundQueue.EnqueueAsync(entry);
            }
            catch (InvalidOperationException ex)
            {
                // Queue is full — return 503 Service Unavailable
                _logger.LogWarning(ex, "Background task queue is full");
                var queueFullDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.3",
                    Title = "Service Temporarily Unavailable",
                    Detail = "The server is currently processing too many requests. Please try again in a moment.",
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Instance = HttpContext.Request.Path
                };
                queueFullDetails.Extensions["requestId"] = HttpContext.TraceIdentifier;
                queueFullDetails.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
                return StatusCode(503, queueFullDetails);
            }

            var location = $"/api/tasks/{entry.Id}";
            // Return requestId for consistency with E2E expectations
            return Accepted(location, new { taskId = entry.Id, requestId = entry.Id, location });
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
