using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/process")]
public class ProcessController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly ILogger<ProcessController> _logger;
    private readonly IConfiguration _configuration;

    public ProcessController(IAIService aiService, ILogger<ProcessController> logger, IConfiguration configuration)
    {
        _aiService = aiService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Send a voice transcript to the AI service and return the response.
    /// </summary>
    [HttpPost]
    [Route("preview")]
    public async Task<IActionResult> Preview([FromBody] ProcessRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Input))
            return BadRequest(new { error = "Input is required" });

        try
        {
            var willUse = await _aiService.WillUseAdvancedModelAsync(request.Input, cancellationToken);
            return Ok(new { willUseAdvanced = willUse });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview failed");
            return StatusCode(500, new { error = "Preview failed" });
        }
    }

    [HttpGet]
    [Route("client-settings")]
    public IActionResult GetClientSettings()
    {
        var configuredMode = _configuration["AI:SpeechTranscriptionMode"]?.Trim().ToLowerInvariant();
        var mode = configuredMode switch
        {
            "webspeech-only" => "webspeech-only",
            "whisper-only" => "whisper-only",
            _ => "auto"
        };

        return Ok(new { speechTranscriptionMode = mode });
    }

    [HttpPost]
    public async Task<IActionResult> Process([FromBody] ProcessRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Input))
            return BadRequest(new { error = "Input is required" });

        try
        {
            // usedAdvancedModel is not relevant for the UI — discard it
            var (task, response, usedAdvanced) = await _aiService.ProcessRequestAsync(request.Input, cancellationToken);
            return Ok(new { task, response, usedAdvancedModel = usedAdvanced });
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
