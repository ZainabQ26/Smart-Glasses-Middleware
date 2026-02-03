using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(IHistoryService historyService, ILogger<HistoryController> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<RequestHistory>>> GetRecentHistory([FromQuery] int count = 10)
    {
        try
        {
            var history = await _historyService.GetRecentHistoryAsync(count);
            if (history == null || !history.Any())
            {
                var pd = new ProblemDetails
                {
                    Type = "https://example.com/probs/not-found",
                    Title = "Not Found",
                    Status = StatusCodes.Status404NotFound,
                    Detail = "No history entries found.",
                    Instance = HttpContext?.Request?.Path
                };
                pd.Extensions["requestId"] = HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
                pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
                return NotFound(pd);
            }
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent history");
            var pd = new ProblemDetails { Title = "Internal Server Error", Detail = "An error occurred retrieving history", Status = StatusCodes.Status500InternalServerError };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            return StatusCode(500, pd);
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RequestHistory>> GetHistoryById(Guid id)
    {
        try
        {
            var history = await _historyService.GetHistoryByIdAsync(id);
            if (history == null)
            {
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = "History entry not found", Status = StatusCodes.Status404NotFound });
            }
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history by ID: {Id}", id);
            var pd = new ProblemDetails { Title = "Internal Server Error", Detail = "An error occurred retrieving history", Status = StatusCodes.Status500InternalServerError };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            return StatusCode(500, pd);
        }
    }

    [HttpGet("reminders")]
    public async Task<ActionResult<List<RequestHistory>>> GetReminders([FromQuery] int count = 100)
    {
        try
        {
            var reminders = await _historyService.GetHistoryByTypeAsync("reminder", count);
            // Return empty array instead of 404 for better API design
            return Ok(reminders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reminders");
            var pd = new ProblemDetails 
            { 
                Title = "Internal Server Error", 
                Detail = "An error occurred retrieving reminders", 
                Status = StatusCodes.Status500InternalServerError 
            };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
            return StatusCode(500, pd);
        }
    }

    [HttpGet("type/{requestType}")]
    public async Task<ActionResult<List<RequestHistory>>> GetHistoryByType(string requestType, [FromQuery] int count = 100)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(requestType))
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Bad Request", 
                    Detail = "Request type cannot be empty", 
                    Status = StatusCodes.Status400BadRequest 
                });
            }

            var history = await _historyService.GetHistoryByTypeAsync(requestType, count);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history by type: {Type}", requestType);
            var pd = new ProblemDetails 
            { 
                Title = "Internal Server Error", 
                Detail = "An error occurred retrieving history", 
                Status = StatusCodes.Status500InternalServerError 
            };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
            return StatusCode(500, pd);
        }
    }
}
