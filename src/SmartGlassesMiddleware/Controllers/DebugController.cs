using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Internal diagnostic and debugging utilities.
/// These endpoints are intended for development and operations use only.
/// </summary>
[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly ITaskDispatcher _taskDispatcher;
    private readonly IConfiguration _configuration;

    public DebugController(ITaskDispatcher taskDispatcher, IConfiguration configuration)
    {
        _taskDispatcher = taskDispatcher;
        _configuration = configuration;
    }

    /// <summary>
    /// Returns a snapshot of all registered task handlers and known task names,
    /// highlighting any mismatches between the <c>AI:KnownTasks</c> configuration list
    /// and the handlers that are actually loaded.
    /// </summary>
    [HttpGet("tasks/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult TaskStatus()
    {
        var knownTasks = _taskDispatcher.KnownTasks
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var handlerMap = _taskDispatcher.GetHandlerTypeMap();
        var handlerTasks = handlerMap.Keys
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var missingHandlersForKnown = knownTasks
            .Where(t => !handlerMap.ContainsKey(t))
            .ToList();

        var handlersNotInKnown = handlerTasks
            .Where(t => !knownTasks.Contains(t))
            .ToList();

        var useAdvancedModel = _configuration.GetValue<bool?>("AI:UseAdvancedModel") ?? false;
        var advancedModelId = _configuration["AWS:Bedrock:AdvancedModelId"];
        var defaultModelId = _configuration["AWS:Bedrock:ModelId"];
        var advancedBedrockConfigured = !string.IsNullOrWhiteSpace(advancedModelId) || !string.IsNullOrWhiteSpace(defaultModelId);

        return Ok(new
        {
            knownTaskCount = knownTasks.Count,
            handlerTaskCount = handlerTasks.Count,
            allKnownTasksHaveHandlers = missingHandlersForKnown.Count == 0,
            knownTasks,
            missingHandlersForKnown,
            handlersNotInKnown,
            handlerMap,
            advanced = new
            {
                useAdvancedModel,
                advancedBedrockConfigured,
                advancedModelId,
                defaultModelId,
                advancedTaskRegistered = handlerMap.ContainsKey("advanced")
            }
        });
    }

    /// <summary>
    /// Echoes back all form fields and file metadata from a multipart/form-data request.
    /// Useful for verifying that the client is sending form data correctly.
    /// </summary>
    [HttpPost("echo-form")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> EchoForm()
    {
        try
        {
            if (!Request.HasFormContentType)
            {
                return BadRequest(new { error = "Not a form content type" });
            }

            var form = await Request.ReadFormAsync();
            var fields = form.Keys.ToDictionary(k => k, k => form[k].ToString());
            var files = form.Files.Select(f => new { f.FileName, f.Name, f.Length, f.ContentType }).ToList();
            return Ok(new { fields, filesCount = files.Count, files });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reads and discards the raw request body, returning only the byte count.
    /// Useful for verifying that large bodies reach the server intact.
    /// </summary>
    [HttpPost("echo-raw")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> EchoRaw()
    {
        try
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            return Ok(new { length = ms.Length });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
