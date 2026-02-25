using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public class AdvancedTaskHandler : ITaskHandler
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdvancedTaskHandler> _logger;

    public AdvancedTaskHandler(IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<AdvancedTaskHandler> logger)
    {
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => "advanced";

    public async Task<string> HandleAsync(string input)
    {
        var endpoint = _configuration["AI:AdvancedModelEndpoint"];
        var model = _configuration["AI:AdvancedModelName"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Advanced model endpoint not configured (AI:AdvancedModelEndpoint)");
            return "Advanced tasks are unavailable: endpoint is not configured.";
        }

        try
        {
            var client = _httpFactory.CreateClient();
            var requestBody = new { message = input, model = model };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Advanced model returned {Status} for endpoint {Endpoint}", response.StatusCode, endpoint);
                return $"Advanced tasks are unavailable: endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
            }

            var body = await response.Content.ReadAsStringAsync();
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("response", out var r))
                {
                    return r.GetString() ?? body;
                }
            }
            catch { /* ignore parse errors */ }

            return body;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling advanced model endpoint");
            return "Advanced tasks are unavailable: failed to reach the endpoint.";
        }
    }
}
