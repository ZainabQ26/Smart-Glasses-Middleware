using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Local task handler for RAG-based document queries.
/// </summary>
public class DocumentQueryTaskHandler : ITaskHandler
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentQueryTaskHandler> _logger;

    public string Name => "document-query";

    public IReadOnlyList<string> Keywords =>
    new[]
    {
        "ask the document",
        "preguntar al documento",
        "query document",
        "consultar documento",
        "what does the document say",
        "qué dice el documento",
        "summarize document",
        "resumir documento",
        "explain in the document",
        "explicar en el documento",
        "according to the document",
        "según el documento",
        "based on the document",
        "basado en el documento"
    };

    public DocumentQueryTaskHandler(IConfiguration configuration, ILogger<DocumentQueryTaskHandler> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            _logger.LogInformation("Processing document query: {Query}", input);

            var apiKey = _configuration["AI:Groq:ApiKey"]?.Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var candidateModels = new[]
                {
                    _configuration["AI:Groq:CompletionModel"],
                    "groq/compound-mini",
                    "openai/gpt-oss-120b"
                }.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!.Trim()).Distinct().ToList();

                var prompt = $"You are an AI assistant analyzing a document for smart glasses. Answer the user's request accurately, clearly, and concisely:\n\n{input}";

                foreach (var model in candidateModels)
                {
                    try
                    {
                        var payload = new
                        {
                            model,
                            messages = new[] { new { role = "user", content = prompt } }
                        };

                        using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                        using var response = await http.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(json);
                            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                        }
                        else
                        {
                            var errBody = await response.Content.ReadAsStringAsync();
                            _logger.LogWarning("Groq API error for model {Model} ({Status}): {Body}", model, response.StatusCode, errBody);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Groq query attempt failed for model {Model}", model);
                    }
                }
            }

            return "Could not process document summary. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document query");
            return $"Error querying documents: {ex.Message}";
        }
    }
}
