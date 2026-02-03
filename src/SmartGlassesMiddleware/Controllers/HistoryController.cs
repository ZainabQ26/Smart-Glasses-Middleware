using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly IHistoryService _historyService;
    private readonly IRAGService _ragService;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(
        IHistoryService historyService, 
        IRAGService ragService,
        ILogger<HistoryController> logger)
    {
        _historyService = historyService;
        _ragService = ragService;
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
            // Service handles null/whitespace validation internally
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

    [HttpPut("reminders/{id}")]
    public async Task<ActionResult<RequestHistory>> UpdateReminder(Guid id, [FromBody] UpdateReminderRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad Request",
                    Detail = "Reminder text is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var updatedReminder = await _historyService.UpdateReminderAsync(id, request.Text);
            
            if (updatedReminder == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Not Found",
                    Detail = $"Reminder with ID {id} not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return Ok(updatedReminder);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid input for updating reminder {Id}", id);
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation for updating reminder {Id}", id);
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating reminder: {Id}", id);
            var pd = new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred updating the reminder",
                Status = StatusCodes.Status500InternalServerError
            };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
            return StatusCode(500, pd);
        }
    }

    [HttpDelete("reminders/{id}")]
    public async Task<ActionResult> DeleteReminder(Guid id)
    {
        try
        {
            var deleted = await _historyService.DeleteReminderAsync(id);
            
            if (!deleted)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Not Found",
                    Detail = $"Reminder with ID {id} not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return NoContent(); // 204 No Content - successful deletion
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation for deleting reminder {Id}", id);
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting reminder: {Id}", id);
            var pd = new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred deleting the reminder",
                Status = StatusCodes.Status500InternalServerError
            };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
            return StatusCode(500, pd);
        }
    }

    [HttpPost("reminders/query")]
    public async Task<ActionResult<QueryResult>> QueryReminders([FromBody] QueryRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad Request",
                    Detail = "Query text is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var result = await _ragService.QueryRemindersAsync(request.Query, request.TopK ?? 10);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying reminders with query: {Query}", request?.Query);
            var pd = new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred querying reminders",
                Status = StatusCodes.Status500InternalServerError
            };
            pd.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pd.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
            return StatusCode(500, pd);
        }
    }
}

public class UpdateReminderRequest
{
    public string Text { get; set; } = string.Empty;
}
