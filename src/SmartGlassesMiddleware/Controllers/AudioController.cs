using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ILogger<AudioController> _logger;

    public AudioController(
        IAIService aiService,
        IHistoryService historyService,
        IMediaStorageService mediaStorageService,
        ILogger<AudioController> logger)
    {
        _aiService = aiService;
        _historyService = historyService;
        _mediaStorageService = mediaStorageService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> ProcessAudio([FromBody] AudioRequest request)
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
            var storageKey = await _mediaStorageService.UploadMediaAsync(
                audioStream,
                $"audio_{DateTime.UtcNow:yyyyMMddHHmmss}.wav",
                "audio/wav");

            // Use transcription if provided, otherwise use a placeholder
            var textToProcess = request.Transcription ?? "[Audio content - transcription needed]";

            // Process with AI
            var (task, response, usedStrongerModel) = await _aiService.ProcessRequestAsync(textToProcess);

            // Save to history
            var history = new RequestHistory
            {
                RequestType = "Audio",
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
            _logger.LogError(ex, "Error processing audio request");
            return StatusCode(500, "An error occurred processing your request");
        }
    }
}
