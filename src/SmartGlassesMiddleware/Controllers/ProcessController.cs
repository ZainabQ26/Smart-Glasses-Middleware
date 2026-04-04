using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/process")]
public class ProcessController : ControllerBase
{
    private readonly IAIService _aiService;
    private readonly IPageSummaryService _pageSummaryService;
    private readonly IServerSettingsService? _serverSettingsService;
    private readonly ILogger<ProcessController> _logger;
    private readonly IConfiguration _configuration;

    public ProcessController(IAIService aiService, IPageSummaryService pageSummaryService, ILogger<ProcessController> logger, IConfiguration configuration)
    {
        _aiService = aiService;
        _pageSummaryService = pageSummaryService;
        _logger = logger;
        _configuration = configuration;
    }

    public ProcessController(IAIService aiService, IPageSummaryService pageSummaryService, IServerSettingsService serverSettingsService, ILogger<ProcessController> logger, IConfiguration configuration)
        : this(aiService, pageSummaryService, logger, configuration)
    {
        _serverSettingsService = serverSettingsService;
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
    public async Task<IActionResult> GetClientSettings(CancellationToken cancellationToken)
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
            activationWord = "Hi Iris";

        var groqEnabled = true;
        if (_serverSettingsService is not null)
        {
            var groqSetting = await _serverSettingsService.GetGroqCommandTranscriptionSettingAsync(cancellationToken);
            groqEnabled = groqSetting.Enabled;
        }

        return Ok(new { speechTranscriptionMode = mode, activationWord, groqCommandTranscriptionEnabled = groqEnabled });
    }

    [HttpPost]
    [Route("transcribe-command")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> TranscribeCommand([FromForm] IFormFile? audio, [FromForm] string? language, CancellationToken cancellationToken)
    {
        if (audio is null || audio.Length == 0)
            return BadRequest(new { error = "Audio is required" });

        if (_serverSettingsService is not null)
        {
            var groqSetting = await _serverSettingsService.GetGroqCommandTranscriptionSettingAsync(cancellationToken);
            if (!groqSetting.Enabled)
                return StatusCode(503, new { error = "Groq transcription is disabled" });
        }

        var configuredGroqEnabled = _configuration["AI:Groq:Enabled"]?.Trim();
        if (bool.TryParse(configuredGroqEnabled, out var configEnabled) && !configEnabled)
            return StatusCode(503, new { error = "Groq transcription is disabled" });

        var apiKey = _configuration["AI:Groq:ApiKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, new { error = "Groq transcription is not configured" });

        var endpoint = _configuration["AI:Groq:WhisperEndpoint"]?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = "https://api.groq.com/openai/v1/audio/transcriptions";

        var model = _configuration["AI:Groq:WhisperModel"]?.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = "whisper-large-v3-turbo";

        try
        {
            var factory = HttpContext.RequestServices.GetService<IHttpClientFactory>();
            using var http = factory?.CreateClient() ?? new HttpClient();

            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var content = new MultipartFormDataContent();
            await using var stream = audio.OpenReadStream();
            using var streamContent = new StreamContent(stream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(audio.ContentType ?? "audio/wav");
            content.Add(streamContent, "file", string.IsNullOrWhiteSpace(audio.FileName) ? "command.wav" : audio.FileName);
            content.Add(new StringContent(model), "model");

            if (!string.IsNullOrWhiteSpace(language))
                content.Add(new StringContent(language), "language");

            content.Add(new StringContent("json"), "response_format");

            using var response = await http.PostAsync(endpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Groq transcription failed ({StatusCode}): {Body}", (int)response.StatusCode, body);
                return StatusCode(502, new { error = "Groq transcription failed" });
            }

            using var jsonDoc = JsonDocument.Parse(body);
            var text = jsonDoc.RootElement.TryGetProperty("text", out var textElement)
                ? (textElement.GetString() ?? string.Empty).Trim()
                : string.Empty;

            return Ok(new { text, provider = "groq", model });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, new { error = "Groq transcription timed out or was cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Groq command transcription unavailable; caller should fallback to local Whisper.");
            return StatusCode(503, new { error = "Groq transcription unavailable" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Process([FromBody] ProcessRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Input))
            return BadRequest(new { error = "Input is required" });

        try
        {
            // usedAdvancedModel is not relevant for the UI — discard it
            var (task, response, usedAdvanced) = await _aiService.ProcessRequestAsync(request.Input, cancellationToken, request.DocumentContext);
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
    public ProcessDocumentContext? DocumentContext { get; set; }
}

public class PageSummaryRequest
{
    public string PageText { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public string? DocumentName { get; set; }
}
