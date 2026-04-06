using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class ReminderTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<ReminderTaskHandler> _logger;
    private readonly IRequestMonitoringService? _monitoringService;

    public string Name => "reminder";

    public IReadOnlyList<string> Keywords => new[] { "remind me" };

    public ReminderTaskHandler(IHistoryService historyService, ILogger<ReminderTaskHandler> logger, IRequestMonitoringService? monitoringService = null)
    {
        _historyService = historyService;
        _logger = logger;
        _monitoringService = monitoringService;
    }

    public async Task<string> HandleAsync(string input)
    {
        string? reqId = null;
        if (_monitoringService != null)
        {
            reqId = await _monitoringService.StartRequest("Task", "reminder", "system");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "I didn't catch that reminder.";
            }

            if (input.Length > 1000)
            {
                return "That reminder is too long.";
            }

            var keywordIndex = input.ToLowerInvariant().IndexOf("remind me");
            string reminderText = input;
            if (keywordIndex != -1)
            {
                var startIndex = keywordIndex + "remind me".Length;
                if (startIndex < input.Length && input[startIndex] == ' ')
                {
                    startIndex++;
                }
                reminderText = input.Substring(startIndex).Trim();
            }

            if (string.IsNullOrWhiteSpace(reminderText))
            {
                return "I didn't catch that reminder.";
            }
            if (reminderText.StartsWith("to "))
            {
                reminderText = reminderText.Substring(3);
            }

            var history = new RequestHistory
            {
                RequestType = "reminder",
                InputText = input, // Store the raw input text, matching tests
                Response = "OK",
                Timestamp = DateTime.UtcNow
            };

            var saved = await _historyService.SaveRequestAsync(history);
            return $"Okay, I'll remind you to {reminderText}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save reminder");
            return "I couldn't save that reminder.";
        }
        finally
        {
            if (_monitoringService != null && reqId != null)
            {
                await _monitoringService.CompleteRequest(reqId, true, "system", "system");
            }
        }
    }
}
