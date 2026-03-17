using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/process")]
public class ProcessController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IPageSummaryService _pageSummaryService;
    private readonly ILogger<ProcessController> _logger;
    private readonly IConfiguration _configuration;

    public ProcessController(IAIService aiService, IPageSummaryService pageSummaryService, ILogger<ProcessController> logger, IConfiguration configuration)
    {
        _aiService = aiService;
        _pageSummaryService = pageSummaryService;
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

        var activationWord = _configuration["AI:ActivationWord"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(activationWord))
            activationWord = "activate";

        return Ok(new { speechTranscriptionMode = mode, activationWord });
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

    [HttpPost]
    [Route("summarize-page")]
    public async Task<IActionResult> SummarizePage([FromBody] PageSummaryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.PageText))
            return BadRequest(new { error = "Page text is required" });

        try
        {
            var summary = await _pageSummaryService.SummarizePageAsync(
                request.PageText,
                request.DocumentName,
                request.PageNumber,
                cancellationToken);

            return Ok(new { summary, usedAdvancedModel = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Page summary request could not be completed");
            return StatusCode(503, new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, new { error = "Request timed out or was cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing current page");
            return StatusCode(500, new { error = "Failed to summarize page" });
        }
    }
}

public class ProcessRequest
{
    public string Input { get; set; } = string.Empty;
}

public class PageSummaryRequest
{
    public string PageText { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public string? DocumentName { get; set; }
}
