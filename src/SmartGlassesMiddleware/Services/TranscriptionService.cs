using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public interface ITranscriptionService
{
    Task<string?> TranscribeAsync(Stream audioStream, string fileName, string contentType, CancellationToken cancellationToken = default);
}

public class HttpTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly ILogger<HttpTranscriptionService> _logger;

    public HttpTranscriptionService(HttpClient httpClient, IConfiguration configuration, ILogger<HttpTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _endpoint = configuration["Transcription:Endpoint"] ?? "http://localhost:8935/transcriptions"; // configurable; set to your transcription service endpoint
    }

    public async Task<string?> TranscribeAsync(Stream audioStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            audioStream.Position = 0;
            var streamContent = new StreamContent(audioStream);
            if (!string.IsNullOrEmpty(contentType)) streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);

            var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transcription service returned {StatusCode}: {ReasonPhrase}", 
                    response.StatusCode, response.ReasonPhrase);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Try to parse JSON with common fields
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(responseContent);
                if (json.ValueKind == JsonValueKind.Object)
                {
                    if (json.TryGetProperty("text", out var t)) return t.GetString();
                    if (json.TryGetProperty("transcription", out var tr)) return tr.GetString();
                    if (json.TryGetProperty("result", out var r)) return r.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse transcription JSON; falling back to raw response.");
            }

            // Fallback: return raw body
            return responseContent;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error calling transcription endpoint: {Endpoint}. Service may be unavailable.", _endpoint);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Transcription request timed out for endpoint: {Endpoint}", _endpoint);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling transcription endpoint: {Endpoint}", _endpoint);
            return null;
        }
    }
}
