using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Provides status polling for background processing tasks.
/// </summary>
[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly IBackgroundTaskQueue _queue;

    public TasksController(IBackgroundTaskQueue queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// Get the current status and result of a background task.
    /// </summary>
    /// <param name="id">The unique identifier of the background task.</param>
    /// <returns>
    /// <c>200 OK</c> when the task completed successfully,
    /// <c>202 Accepted</c> while it is still in progress,
    /// <c>404 Not Found</c> if the task ID is unknown, or
    /// <c>500 Internal Server Error</c> if the task failed.
    /// </returns>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult GetTask(Guid id)
    {
        try
        {
            var entry = _queue.Get(id);
            if (entry == null) return NotFound(new ProblemDetails { Title = "Not Found", Detail = "Task not found", Status = StatusCodes.Status404NotFound });

            var result = new
            {
                id = entry.Id,
                status = entry.Status.ToString(),
                createdAt = entry.CreatedAt,
                updatedAt = entry.UpdatedAt,
                error = entry.Error,
                result = entry.Result
            };

            if (entry.Status == BackgroundTaskStatus.Completed)
                return Ok(result);
            if (entry.Status == BackgroundTaskStatus.Failed)
                return StatusCode(500, result);

            return Accepted($"/api/tasks/{entry.Id}", result);
        }
        catch (Exception)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = "Task not found", Status = StatusCodes.Status404NotFound });
        }
    }
}
