using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/ask")]
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

            string? promptAudioTranscription = null;
            // Aggregate files from the strongly-typed model or raw form parts (guard form access)
            var files = new List<IFormFile>();
            if (request.Files?.Any() == true)
            {
                files.AddRange(request.Files!);
            }
            else if (Request.HasFormContentType && Request.Form.Files?.Count > 0)
            {
                files.AddRange(Request.Form.Files.ToList());
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
                            Type = "https://example.com/probs/validation",
                            Title = "Validation Failed",
                            Status = StatusCodes.Status400BadRequest,
                            Detail = "Provide either 'prompt' (text) or 'promptAudio' (file), not both.",
                            Instance = HttpContext?.Request?.Path
                        };

                        pd.Extensions["requestId"] = Guid.NewGuid().ToString();
                        pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");

                        return BadRequest(pd);
                    }

                    try
                    {
                        using var promptStream = promptAudioFile.OpenReadStream();
                        var transcribedPrompt = await _transcriptionService.TranscribeAsync(promptStream, promptAudioFile.FileName, promptAudioFile.ContentType);
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
            var inputText = Request.HasFormContentType && Request.Form.TryGetValue("input", out var iv) ? iv.FirstOrDefault() : null;
            IFormFile? inputFile = null;
            if (Request.HasFormContentType)
            {
                var formFiles = Request.Form?.Files;
                if (formFiles != null && formFiles.Count > 0)
                {
                    inputFile = formFiles.FirstOrDefault(f => string.Equals(f.Name, "input", StringComparison.OrdinalIgnoreCase));
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

            // Process each file in order: upload, optionally transcribe, collect metadata
            var mediaItems = new List<object>();
            var partsForAi = new List<string>();
            foreach (var file in orderedFiles)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest(new ProblemDetails { Title = "Bad Request", Detail = $"Invalid file type: {file.FileName}", Status = StatusCodes.Status400BadRequest });
                }

                using var uploadStream = file.OpenReadStream();
                var storageKey = await _mediaStorageService.UploadMediaAsync(uploadStream, file.FileName, file.ContentType);

                string? thisTranscription = null;
                // If per-file transcription exists for this file, prefer it
                if (perFileTrans != null && perFileTrans.TryGetValue(file.FileName, out var mapped))
                {
                    thisTranscription = mapped;
                }
                if (audioExtensions.Contains(extension))
                {
                    // If a per-file transcription exists we already set it above. Otherwise transcribe the audio.
                    if (thisTranscription == null)
                    {
                        try
                        {
                            using var downloaded = await _mediaStorageService.DownloadMediaAsync(storageKey);
                            thisTranscription = await _transcriptionService.TranscribeAsync(downloaded, file.FileName, file.ContentType);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Transcription failed for {Key}", storageKey);
                        }
                    }
                }

                mediaItems.Add(new { fileName = file.FileName, contentType = file.ContentType, storageKey, transcription = thisTranscription });

                var partText = $"MediaFileName: {file.FileName}\nMediaStorageKey: {storageKey}\nMediaContentType: {file.ContentType}";
                if (!string.IsNullOrWhiteSpace(thisTranscription)) partText += $"\nTranscription: {thisTranscription}";
                partsForAi.Add(partText);
            }

            // Build final prompt for AI
            var promptText = request.Prompt ?? string.Empty;

            // If a prompt audio file was uploaded as `promptAudio`, transcribe it and use that as the prompt (override text prompt)
            if (Request.HasFormContentType)
            {
                var formFiles = Request.Form?.Files;
                var promptAudioFile = formFiles?.FirstOrDefault(f => string.Equals(f.Name, "promptAudio", StringComparison.OrdinalIgnoreCase));
                if (promptAudioFile != null)
                {
                    try
                    {
                        using var promptStream = promptAudioFile.OpenReadStream();
                        var transcribedPrompt = await _transcriptionService.TranscribeAsync(promptStream, promptAudioFile.FileName, promptAudioFile.ContentType);
                        if (!string.IsNullOrWhiteSpace(transcribedPrompt))
                        {
                            promptText = transcribedPrompt;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to transcribe prompt audio");
                    }
                }
            }

            var textToProcess = promptText + "\n\n" + string.Join("\n\n", partsForAi);

            // Process with AI
            var (task, response, usedAdvancedModel) = await _aiService.ProcessRequestAsync(textToProcess);

            // Save to history
            try
            {
                var history = new RequestHistory
                {
                    RequestType = "Media",
                    InputText = textToProcess,
                    DetectedTask = task,
                    Response = response,
                    UsedAdvancedModel = usedAdvancedModel,
                    MediaStorageKey = string.Join(",", ((IEnumerable<dynamic>)mediaItems).Select(m => (string)m.storageKey))
                };
                history = await _historyService.SaveRequestAsync(history);

                return Ok(new { task, response, usedAdvancedModel, requestId = history.Id, items = mediaItems });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save history for media upload");
                return Ok(new { task, response, usedAdvancedModel, items = mediaItems });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing media request");
            var pd = new ProblemDetails { Title = "Internal Server Error", Detail = "An error occurred processing your request", Status = StatusCodes.Status500InternalServerError };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            return StatusCode(500, pd);
        }
    }
}
