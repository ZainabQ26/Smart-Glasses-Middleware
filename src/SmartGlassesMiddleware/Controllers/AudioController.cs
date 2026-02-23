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
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;

    public AudioController(
        ITranscriptionService transcriptionService,
        IHistoryService historyService,
        ILogger<AudioController> logger,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        IRequestMonitoringService? monitoringService = null)
    {
        _transcriptionService = transcriptionService;
        _historyService = historyService;
        _logger = logger;
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
        _monitoringService = monitoringService;
    }

    /// <summary>
    /// Transcribes audio from base64-encoded data. Returns immediately with a task ID;
    /// poll GET /api/tasks/{id} for the result.
    /// </summary>
    /// <param name="request">Audio request containing base64-encoded audio data</param>
    /// <returns>202 Accepted with taskId for polling</returns>
    /// <response code="202">Task queued — poll /api/tasks/{taskId} for result</response>
    /// <response code="400">Invalid audio data or request format</response>
    /// <response code="500">Internal server error during processing</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
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

            // Queue transcription as a background task and return 202 immediately
            // so we never block longer than CloudFront's 60 s origin timeout.
            var entry = new BackgroundTaskEntry
            {
                Status = BackgroundTaskStatus.Queued,
                MonitoringRequestId = monitoringRequestId,
                PromptText = request.MimeType ?? "audio/wav",   // re-use for mime type
            };

            // Persist the entry so /api/tasks/{id} can find it before work starts
            await _taskQueue.UpdateAsync(entry);

            if (_monitoringService != null && monitoringRequestId != null)
                await _monitoringService.UpdateRequestStatus(monitoringRequestId, "Queued", "Transcription queued", 10);

            // Capture values for the closure
            var taskId = entry.Id;
            var audioBytesCopy = audioBytes;
            var mimeType = request.MimeType ?? "audio/wav";
            var scopeFactory = _scopeFactory;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var transcriptionSvc = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();
                    var historySvc = scope.ServiceProvider.GetRequiredService<IHistoryService>();
                    var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();

                    // Fetch the stored entry so we update the same object in the store
                    var taskEntry = queue.Get(taskId) ?? new BackgroundTaskEntry { Id = taskId };
                    taskEntry.Status = BackgroundTaskStatus.Processing;
                    taskEntry.UpdatedAt = DateTime.UtcNow;
                    await queue.UpdateAsync(taskEntry);

                    var fileName = $"audio_{DateTime.UtcNow:yyyyMMddHHmmss}";
                    using var audioStream = new MemoryStream(audioBytesCopy);
                    var transcription = await transcriptionSvc.TranscribeAsync(audioStream, fileName, mimeType);

                    // Save history
                    try
                    {
                        var history = new RequestHistory
                        {
                            RequestType = "AudioTranscription",
                            InputText = transcription ?? string.Empty,
                            MediaStorageKey = null
                        };
                        if (!string.IsNullOrWhiteSpace(monitoringRequestId))
                            history.CorrelationId = monitoringRequestId;
                        await historySvc.SaveRequestAsync(history);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to save transcription history for task {TaskId}", taskId);
                    }

                    taskEntry.Result = new { transcription };
                    taskEntry.Status = BackgroundTaskStatus.Completed;
                    taskEntry.UpdatedAt = DateTime.UtcNow;
                    await queue.UpdateAsync(taskEntry);
                    logger.LogInformation("Async transcription task {TaskId} completed", taskId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Async transcription task {TaskId} failed", taskId);
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
                        var taskEntry = queue.Get(taskId) ?? new BackgroundTaskEntry { Id = taskId };
                        taskEntry.Error = ex.Message;
                        taskEntry.Status = BackgroundTaskStatus.Failed;
                        taskEntry.UpdatedAt = DateTime.UtcNow;
                        await queue.UpdateAsync(taskEntry);
                    }
                    catch { /* best-effort */ }
                }
            });

            return Accepted($"/api/tasks/{taskId}", new { taskId });
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
