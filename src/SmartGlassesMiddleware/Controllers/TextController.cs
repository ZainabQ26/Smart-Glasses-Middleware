using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TextController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<TextController> _logger;

    public TextController(
        IAIService aiService,
        IHistoryService historyService,
        ICacheService cacheService,
        ILogger<TextController> logger)
    {
        _aiService = aiService;
        _historyService = historyService;
        _cacheService = cacheService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> ProcessText([FromBody] TaskRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest("Text cannot be empty");
            }

            // Check cache first
            var cacheKey = $"text:{request.Text.GetHashCode()}";
            var cachedResponse = await _cacheService.GetAsync<TaskResponse>(cacheKey);
            if (cachedResponse != null)
            {
                _logger.LogInformation("Returning cached response for text request");
                return Ok(cachedResponse);
            }

            // Process with AI
            var (task, response, usedStrongerModel) = await _aiService.ProcessRequestAsync(request.Text);

            // Save to history
            var history = new RequestHistory
            {
                RequestType = "Text",
                InputText = request.Text,
                DetectedTask = task,
                Response = response,
                UsedStrongerModel = usedStrongerModel
            };
            history = await _historyService.SaveRequestAsync(history);

            var taskResponse = new TaskResponse
            {
                Task = task,
                Response = response,
                UsedStrongerModel = usedStrongerModel,
                RequestId = history.Id
            };

            // Cache the response for 5 minutes
            await _cacheService.SetAsync(cacheKey, taskResponse, TimeSpan.FromMinutes(5));

            return Ok(taskResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing text request");
            return StatusCode(500, "An error occurred processing your request");
        }
    }
}
