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
    private readonly ILogger<MediaController> _logger;

    public MediaController(
        IAIService aiService,
        IHistoryService historyService,
        IMediaStorageService mediaStorageService,
        ILogger<MediaController> logger)
    {
        _aiService = aiService;
        _historyService = historyService;
        _mediaStorageService = mediaStorageService;
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

            // Validate file type
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".avi", ".mov" };
            var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest("Invalid file type. Supported types: images (jpg, png, gif) and videos (mp4, avi, mov)");
            }

            // Store media in MinIO
            using var fileStream = request.File.OpenReadStream();
            var storageKey = await _mediaStorageService.UploadMediaAsync(
                fileStream,
                request.File.FileName,
                request.File.ContentType);

            // Process description with AI if provided
            var textToProcess = request.Description ?? $"[{extension} file uploaded - {request.File.FileName}]";
            var (task, response, usedStrongerModel) = await _aiService.ProcessRequestAsync(textToProcess);

            // Save to history
            var history = new RequestHistory
            {
                RequestType = extension.Contains("mp4") || extension.Contains("avi") || extension.Contains("mov") 
                    ? "Video" 
                    : "Image",
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
