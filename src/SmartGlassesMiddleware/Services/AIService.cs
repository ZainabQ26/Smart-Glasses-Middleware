using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public interface IAIService
{
    Task<(string task, string response, bool usedStrongerModel)> ProcessRequestAsync(string input);
}

public class OllamaAIService : IAIService
{
    private readonly Kernel _kernel;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaAIService> _logger;
    private readonly string _strongerModelEndpoint;
    private readonly List<string> _knownTasks;

    public OllamaAIService(Kernel kernel, HttpClient httpClient, IConfiguration configuration, ILogger<OllamaAIService> logger)
    {
        _kernel = kernel;
        _httpClient = httpClient;
        _logger = logger;
        _strongerModelEndpoint = configuration["AI:StrongerModelEndpoint"] ?? "http://localhost:5000/api/chat";
        
        // Define known tasks that the local model can handle
        _knownTasks = new List<string>
        {
            "weather",
            "time",
            "reminder",
            "alarm",
            "timer",
            "note",
            "translation",
            "calculation"
        };
    }

    public async Task<(string task, string response, bool usedStrongerModel)> ProcessRequestAsync(string input)
    {
        try
        {
            // First, use Ollama to detect the task
            var taskDetectionPrompt = $@"
You are a task classifier. Analyze the following input and determine if it matches one of these known tasks: 
{string.Join(", ", _knownTasks)}.

If it matches a known task, respond with just the task name. 
If it doesn't match any known task, respond with 'UNKNOWN'.

Input: {input}

Task:";

            var chatService = _kernel.GetRequiredService<IChatCompletionService>();
            var taskResult = await chatService.GetChatMessageContentAsync(taskDetectionPrompt);
            var detectedTask = taskResult.Content?.Trim().ToLower() ?? "unknown";

            _logger.LogInformation("Detected task: {Task} for input: {Input}", detectedTask, input);

            // If task is unknown, use stronger model
            if (detectedTask == "unknown" || !_knownTasks.Contains(detectedTask))
            {
                _logger.LogInformation("Task is unknown, forwarding to stronger model");
                var strongerResponse = await CallStrongerModelAsync(input);
                return ("unknown", strongerResponse, true);
            }

            // Otherwise, process with local Ollama model
            var processingPrompt = $@"
You are a helpful assistant processing a {detectedTask} task.

User request: {input}

Please provide a helpful and concise response:";

            var response = await chatService.GetChatMessageContentAsync(processingPrompt);
            
            return (detectedTask, response.Content ?? "I couldn't process that request.", false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request with AI service");
            throw;
        }
    }

    private async Task<string> CallStrongerModelAsync(string input)
    {
        try
        {
            var requestBody = new
            {
                message = input,
                model = "gpt-4"
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(_strongerModelEndpoint, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            return result.GetProperty("response").GetString() ?? "No response from stronger model.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling stronger model");
            return "I'm having trouble connecting to the advanced model. Please try again later.";
        }
    }
}
