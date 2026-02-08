using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartGlassesMiddleware.Services;

public interface IOllamaEmbeddingClient
{
    Task<float[]> GenerateEmbeddingAsync(string model, string input, CancellationToken ct = default);
}

public class OllamaEmbeddingClient : IOllamaEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaEmbeddingClient> _logger;

    public OllamaEmbeddingClient(HttpClient http, ILogger<OllamaEmbeddingClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string model, string input, CancellationToken ct = default)
    {
        var payload = new { model = model, input = input };

        try
        {
            using var resp = await _http.PostAsJsonAsync("/v1/embeddings", payload, ct);
            var content = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama embedding request failed: {Status} {Body}", resp.StatusCode, content);
                resp.EnsureSuccessStatusCode();
            }

            using var doc = JsonDocument.Parse(content);
            // Expecting OpenAI-compatible response: { data: [ { embedding: [...] } ] }
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var first = data[0];
                if (first.TryGetProperty("embedding", out var emb))
                {
                    var list = new List<float>();
                    foreach (var el in emb.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out var f)) list.Add(f);
                        else if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) list.Add((float)d);
                    }
                    return list.ToArray();
                }
            }

            _logger.LogError("Unexpected embedding response format: {Body}", content);
            throw new InvalidOperationException("Unexpected embedding response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get embedding from Ollama");
            throw;
        }
    }
}
