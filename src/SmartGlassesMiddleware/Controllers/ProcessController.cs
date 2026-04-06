using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/process")]
public class ProcessController : ControllerBase
{
    private IAIService AiService => HttpContext.RequestServices.GetRequiredService<IAIService>();
    private IPageSummaryService PageSummaryService => HttpContext.RequestServices.GetRequiredService<IPageSummaryService>();
    private IGroqTranscriptionLogService GroqTranscriptionLog => HttpContext.RequestServices.GetRequiredService<IGroqTranscriptionLogService>();
    private ILogger<ProcessController> Logger => HttpContext.RequestServices.GetRequiredService<ILogger<ProcessController>>();
    private IConfiguration Configuration => HttpContext.RequestServices.GetRequiredService<IConfiguration>();

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
            var willUse = await AiService.WillUseAdvancedModelAsync(request.Input, cancellationToken);
            return Ok(new { willUseAdvanced = willUse });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Preview failed");
            return StatusCode(500, new { error = "Preview failed" });
        }
    }

    // client-settings endpoint moved to minimal API in Program.cs to avoid DI issues

    [HttpPost]
    [Route("transcribe")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Transcribe(
        [FromForm] IFormFile? audio, 
        [FromForm] string? language,
        [FromForm] bool checkWakeword = false,
        CancellationToken cancellationToken = default)
    {
        if (audio is null || audio.Length == 0)
            return BadRequest(new { error = "Audio is required" });

        var apiKey = Configuration["AI:Groq:ApiKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, new { error = "Groq transcription is not configured" });

        var endpoint = Configuration["AI:Groq:WhisperEndpoint"]?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = "https://api.groq.com/openai/v1/audio/transcriptions";

        var model = Configuration["AI:Groq:WhisperModel"]?.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = "whisper-large-v3";

        try
        {
            var factory = HttpContext.RequestServices.GetService<IHttpClientFactory>();
            using var http = factory?.CreateClient() ?? new HttpClient();

            // Use shorter timeout for wake-word detection, longer for full commands
            http.Timeout = TimeSpan.FromSeconds(checkWakeword ? 5 : 20);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var content = new MultipartFormDataContent();
            await using var stream = audio.OpenReadStream();
            using var streamContent = new StreamContent(stream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(audio.ContentType ?? "audio/wav");
            content.Add(streamContent, "file", string.IsNullOrWhiteSpace(audio.FileName) ? "audio.wav" : audio.FileName);
            content.Add(new StringContent(model), "model");

            // For wake-word detection, bias Whisper toward the activation word to reduce hallucination on short clips
            if (checkWakeword)
            {
                var activationWord = Configuration["AI:ActivationWord"]?.Trim();
                if (string.IsNullOrWhiteSpace(activationWord)) activationWord = "Iris";
                content.Add(new StringContent(activationWord), "prompt");
            }

            if (!string.IsNullOrWhiteSpace(language))
                content.Add(new StringContent(language), "language");

            content.Add(new StringContent("json"), "response_format");

            using var response = await http.PostAsync(endpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var logType = checkWakeword ? "wakeword" : "command";
                Logger.LogWarning("Groq {Type} transcription failed ({StatusCode}): {Body}", logType, (int)response.StatusCode, body);
                return StatusCode(502, new { error = "Groq transcription failed" });
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse(body);
                var text = jsonDoc.RootElement.TryGetProperty("text", out var textElement)
                    ? (textElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;

                var logType = checkWakeword ? "wakeword" : "command";
                GroqTranscriptionLog.Add(text, logType);

                // For wake-word detection, check if the transcription contains the activation word
                if (checkWakeword)
                {
                    var activationWord = Configuration["AI:ActivationWord"]?.Trim() ?? "Iris";
                    var normalized = text.ToLowerInvariant().Replace(activationWord.ToLowerInvariant(), "").Trim();
                    var matched = text.ToLowerInvariant().Contains(activationWord.ToLowerInvariant());
                    return Ok(new { text, matched, provider = "groq", model });
                }

                return Ok(new { text, provider = "groq", model });
            }
            catch (JsonException jsonEx)
            {
                Logger.LogWarning(jsonEx, "Failed to parse Groq response as JSON. Response: {Body}", body);
                return StatusCode(502, new { error = "Invalid response from Groq API" });
            }
        }
        catch (OperationCanceledException)
        {
            var timeoutMsg = checkWakeword ? "Groq wakeword transcription timed out" : "Groq transcription timed out or was cancelled";
            return StatusCode(408, new { error = timeoutMsg });
        }
        catch (Exception ex)
        {
            var logType = checkWakeword ? "wakeword" : "command";
            Logger.LogError(ex, "Groq {Type} transcription error.", logType);
            return StatusCode(503, new { error = "Groq transcription unavailable" });
        }
    }

    [HttpPost]
    [Route("transcribe-wakeword")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [Obsolete("Use /transcribe with checkWakeword=true instead")]
    public async Task<IActionResult> TranscribeWakeword([FromForm] IFormFile? audio, [FromForm] string? language, CancellationToken cancellationToken)
    {
        // Redirect to unified endpoint for backward compatibility
        var form = await Request.ReadFormAsync(cancellationToken);
        var audioFile = form.Files.FirstOrDefault();
        var lang = form["language"].FirstOrDefault();
        
        return await Transcribe(audioFile, lang, checkWakeword: true, cancellationToken);
    }

    [HttpPost]
    [Route("transcribe-command")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [Obsolete("Use /transcribe with checkWakeword=false instead")]
    public async Task<IActionResult> TranscribeCommand([FromForm] IFormFile? audio, [FromForm] string? language, CancellationToken cancellationToken)
    {
        // Redirect to unified endpoint for backward compatibility
        var form = await Request.ReadFormAsync(cancellationToken);
        var audioFile = form.Files.FirstOrDefault();
        var lang = form["language"].FirstOrDefault();
        
        return await Transcribe(audioFile, lang, checkWakeword: false, cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Process([FromBody] ProcessRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Input))
            return BadRequest(new { error = "Input is required" });

        try
        {
            // usedAdvancedModel is not relevant for the UI — discard it
            var (task, response, usedAdvanced) = await AiService.ProcessRequestAsync(request.Input, cancellationToken, request.DocumentContext);
            return Ok(new { task, response, usedAdvancedModel = usedAdvanced });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, new { error = "Request timed out or was cancelled" });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing AI request for transcript");
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
            var summary = await PageSummaryService.SummarizePageAsync(
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
            Logger.LogWarning(ex, "Page summary request could not be completed");
            return StatusCode(503, new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, new { error = "Request timed out or was cancelled" });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error summarizing current page");
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
