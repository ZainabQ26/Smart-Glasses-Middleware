using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/process")]
public class ProcessController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly ILogger<ProcessController> _logger;

    public ProcessController(IAIService aiService, ILogger<ProcessController> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    /// <summary>
    /// Send a voice transcript to the AI service and return the response.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Process([FromBody] ProcessRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Input))
            return BadRequest(new { error = "Input is required" });

        try
        {
            // usedAdvancedModel is not relevant for the UI — discard it
            var (task, response, _) = await _aiService.ProcessRequestAsync(request.Input, cancellationToken);
            return Ok(new { task, response });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, new { error = "Request timed out or was cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AI request for transcript");
            return StatusCode(500, new { error = "Failed to process request" });
        }
    }
}

public class ProcessRequest
{
    public string Input { get; set; } = string.Empty;
}
