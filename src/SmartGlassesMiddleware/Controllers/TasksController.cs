using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly IBackgroundTaskQueue _queue;

    public TasksController(IBackgroundTaskQueue queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// Get background task status/result by id for polling clients.
    /// Returns 202 while queued/processing, 200 with result when completed, 500 when failed, 404 if not found.
    /// </summary>
    [HttpGet("{id}")]
    public IActionResult GetTaskStatus(Guid id)
    {
        var entry = _queue.Get(id);
        if (entry == null) return NotFound(new { error = "Task not found" });

        if (entry.Status == BackgroundTaskStatus.Queued || entry.Status == BackgroundTaskStatus.Processing)
        {
            return Accepted(new { taskId = entry.Id, status = entry.Status.ToString(), updatedAt = entry.UpdatedAt });
        }

        if (entry.Status == BackgroundTaskStatus.Completed)
        {
            return Ok(new { taskId = entry.Id, status = entry.Status.ToString(), result = entry.Result, updatedAt = entry.UpdatedAt });
        }

        // Failed
        return StatusCode(500, new { taskId = entry.Id, status = entry.Status.ToString(), error = entry.Error, updatedAt = entry.UpdatedAt });
    }
}
using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly IBackgroundTaskQueue _queue;

    public TasksController(IBackgroundTaskQueue queue)
    {
        _queue = queue;
    }

    [HttpGet("{id}")]
    public ActionResult GetTask(Guid id)
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
        {
            return Ok(result);
        }
        if (entry.Status == BackgroundTaskStatus.Failed)
        {
            return StatusCode(500, result);
        }

        return Accepted($"/api/tasks/{entry.Id}", result);
    }
}
