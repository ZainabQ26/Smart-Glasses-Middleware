using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartGlassesMiddleware.Services;

public class ShowRemindersTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<ShowRemindersTaskHandler> _logger;

    public string Name => "show-reminders";

    public IReadOnlyList<string> Keywords => new[]
    {
        "show reminders", "list reminders", "my reminders", "see reminders",
        "view reminders", "show all reminders", "what are my reminders",
        "show my reminders", "list my reminders"
    };

    public ShowRemindersTaskHandler(IHistoryService historyService, ILogger<ShowRemindersTaskHandler> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            var reminders = await _historyService.GetHistoryByTypeAsync("reminder", 50);
            var ordered = reminders.OrderByDescending(r => r.Timestamp).ToList();

            if (!ordered.Any())
            {
                return "You have no reminders set. Say \"remind me to…\" to create one.";
            }

            var items = ordered.Select(r => new ReminderItem
            {
                Id = r.Id.ToString(),
                Text = r.InputText,
                CreatedAt = r.Timestamp
            }).ToList();

            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            _logger.LogInformation("ShowReminders returning {Count} reminders", items.Count);
            return $"__ACTION:SHOW_REMINDERS:{json}__";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving reminders");
            return "Sorry, I couldn't retrieve your reminders right now. Please try again.";
        }
    }

    private sealed class ReminderItem
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        [JsonConverter(typeof(JsonDateTimeConverter))]
        public DateTime CreatedAt { get; set; }
    }

    private sealed class JsonDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = reader.GetString();
            return !string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, out var dt) ? dt : DateTime.MinValue;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("o"));
    }
}
