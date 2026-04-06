using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class EditReminderTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<EditReminderTaskHandler> _logger;

    public string Name => "edit-reminder";

    public IReadOnlyList<string> Keywords => new[] { "edit reminder", "change" };

    public EditReminderTaskHandler(IHistoryService historyService, ILogger<EditReminderTaskHandler> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    private (string? oldText, string? newText) ParseEditInstruction(string input)
    {
        var inputLower = input.ToLowerInvariant();
        var changeIndex = inputLower.IndexOf("change ");
        if (changeIndex != -1)
        {
            var content = inputLower.Substring(changeIndex + "change ".Length).Trim();
            var toIndex = content.LastIndexOf(" to ");
            if (toIndex != -1)
            {
                var oldText = content.Substring(0, toIndex).Trim();
                var newText = content.Substring(toIndex + " to ".Length).Trim();
                return (oldText, newText);
            }
        }
        return (null, null);
    }

    public async Task<string> HandleAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "I didn't catch that.";
        }

        var (oldText, newText) = ParseEditInstruction(input);

        if (oldText == null || newText == null)
        {
            if (input.ToLowerInvariant().Contains("edit reminder"))
            {
                 return "I couldn't parse what to edit. Try saying 'change X to Y'.";
            }
            return "I couldn't understand what to change. Try saying 'change [old reminder] to [new reminder]'.";
        }

        var reminders = await _historyService.GetHistoryByTypeAsync("reminder", 100);
        var matches = reminders.Where(r => r.InputText.ToLowerInvariant().Contains(oldText)).ToList();

        if (matches.Count == 0)
        {
            return $"I couldn't find a reminder about '{oldText}'. No reminder was updated. You can create a new reminder instead.";
        }

        if (matches.Count > 1)
        {
            return $"I found {matches.Count} reminders matching '{oldText}'. Please be more specific.";
        }

        var match = matches.First();

        try
        {
            var updated = await _historyService.UpdateReminderAsync(match.Id, newText);
            if (updated == null)
            {
                return "I couldn't update that reminder. It might have been deleted.";
            }

            return $"I updated the reminder from '{oldText}' to '{newText}'.";
        }
        catch (ArgumentException ex)
        {
            return $"I couldn't update the reminder: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            return $"I couldn't update the reminder: {ex.Message}";
        }
    }
}
