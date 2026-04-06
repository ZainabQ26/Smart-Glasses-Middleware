using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartGlassesMiddleware.Services;

public class JsonDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTime.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }
}

public class ShowRemindersTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<ShowRemindersTaskHandler> _logger;

    public string Name => "show-reminders";

    public IReadOnlyList<string> Keywords => new[] { "show reminders", "show my reminders" };

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
            if (reminders == null || !reminders.Any())
            {
                return "You have no reminders.";
            }

            var reminderItems = reminders.Select(r => new
            {
                Id = r.Id,
                Text = r.InputText,
                CreatedAt = r.Timestamp
            }).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(reminderItems, new System.Text.Json.JsonSerializerOptions
            {
                Converters = { new JsonDateTimeConverter() }
            });

            return $"__ACTION:SHOW_REMINDERS:{json}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve reminders.");
            return "I had trouble retrieving your reminders.";
        }
    }
}
