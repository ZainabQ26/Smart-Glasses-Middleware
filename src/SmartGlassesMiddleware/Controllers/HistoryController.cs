using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Manages request history entries and reminder CRUD operations.
/// </summary>
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

    /// <summary>
    /// Get the most recent request history entries.
    /// </summary>
    /// <param name="count">Maximum number of entries to return (default: 10).</param>
    /// <returns>A list of recent <see cref="RequestHistory"/> entries.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<RequestHistory>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Get a single history entry by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the history entry.</param>
    /// <returns>The matching <see cref="RequestHistory"/> record.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(RequestHistory), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Get all saved reminders (history entries of type "reminder").
    /// </summary>
    /// <param name="count">Maximum number of reminders to return (default: 100).</param>
    /// <returns>A list of reminder <see cref="RequestHistory"/> entries. Returns an empty array when none exist.</returns>
    [HttpGet("reminders")]
    [ProducesResponseType(typeof(List<RequestHistory>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Get history entries filtered by request type (e.g., "reminder", "document-search").
    /// </summary>
    /// <param name="requestType">The task/request type to filter by.</param>
    /// <param name="count">Maximum number of entries to return (default: 100).</param>
    /// <returns>A list of matching <see cref="RequestHistory"/> entries.</returns>
    [HttpGet("type/{requestType}")]
    [ProducesResponseType(typeof(List<RequestHistory>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Update the text of an existing reminder.
    /// </summary>
    /// <param name="id">The unique identifier of the reminder to update.</param>
    /// <param name="request">Request body containing the new reminder text.</param>
    /// <returns>The updated <see cref="RequestHistory"/> record.</returns>
    [HttpPut("reminders/{id}")]
    [ProducesResponseType(typeof(RequestHistory), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Delete a reminder by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the reminder to delete.</param>
    [HttpDelete("reminders/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    /// Perform a semantic RAG query over saved reminders.
    /// </summary>
    /// <param name="request">Query text and optional result limit.</param>
    /// <returns>A <see cref="QueryResult"/> containing semantically similar reminders.</returns>
    [HttpPost("reminders/query")]
    [ProducesResponseType(typeof(QueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

/// <summary>Request body for updating a reminder's text.</summary>
public class UpdateReminderRequest
{
    /// <summary>The new text for the reminder.</summary>
    public string Text { get; set; } = string.Empty;
}
