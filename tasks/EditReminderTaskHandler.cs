using SmartGlassesMiddleware.Models;
using System.Text.RegularExpressions;

namespace SmartGlassesMiddleware.Services;

public class EditReminderTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<EditReminderTaskHandler> _logger;

    public string Name => "edit-reminder";

    public IReadOnlyList<string> Keywords => new[]
    {
        "edit reminder", "update reminder", "change reminder", "modify reminder"
    };

    public EditReminderTaskHandler(IHistoryService historyService, ILogger<EditReminderTaskHandler> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                _logger.LogWarning("Edit reminder task received empty input");
                return "I didn't catch that. Please tell me which reminder you'd like to edit and what to change it to.";
            }

            // Try to parse the input to extract old text and new text
            // Common patterns:
            // "change [old] to [new]"
            // "update [old] to [new]"
            // "edit [old] to [new]"
            // "change reminder about [old] to [new]"

            var parsedInput = ParseEditCommand(input);

            if (parsedInput.oldText == null || parsedInput.newText == null)
            {
                return "I couldn't understand what you want to edit. Try saying something like 'change my reminder about milk to buy eggs' or 'edit buy milk to buy bread'.";
            }

            // Search for reminders containing the old text
            var allReminders = await _historyService.GetHistoryByTypeAsync("reminder", 100);
            var matchingReminders = allReminders
                .Where(r => r.InputText.Contains(parsedInput.oldText, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Timestamp)
                .ToList();

            if (!matchingReminders.Any())
            {
                return $"I couldn't find any reminders matching '{parsedInput.oldText}'. Would you like to create a new reminder instead?";
            }

            if (matchingReminders.Count > 1)
            {
                var reminderList = string.Join("\n", matchingReminders.Take(5).Select((r, i) =>
                    $"{i + 1}. {r.InputText} (created {r.Timestamp:yyyy-MM-dd HH:mm} UTC)"));
                return $"I found {matchingReminders.Count} reminders matching '{parsedInput.oldText}':\n\n{reminderList}\n\nPlease be more specific about which reminder you want to edit.";
            }

            // Update the reminder
            var reminderToUpdate = matchingReminders[0];
            var updatedReminder = await _historyService.UpdateReminderAsync(reminderToUpdate.Id, parsedInput.newText);

            if (updatedReminder == null)
            {
                return "Sorry, I couldn't update that reminder. It may have been deleted.";
            }

            _logger.LogInformation("Reminder updated successfully via AI: {ReminderId}", updatedReminder.Id);
            return $"Got it! I've updated your reminder from '{reminderToUpdate.InputText}' to '{parsedInput.newText}'.";
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Validation error updating reminder");
            return $"I couldn't update that reminder: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation updating reminder");
            return $"I couldn't update that reminder: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing reminder via AI");
            return "Sorry, something went wrong while trying to update your reminder. Please try again.";
        }
    }

    private (string? oldText, string? newText) ParseEditCommand(string input)
    {
        // Remove common prefixes
        var cleanInput = Regex.Replace(input, @"^(please\s+)?(can you\s+)?(i want to\s+)?(i'd like to\s+)?", "", RegexOptions.IgnoreCase);
        cleanInput = Regex.Replace(cleanInput, @"^(change|edit|update|modify)\s+(my\s+)?(reminder\s+)?(about\s+)?", "", RegexOptions.IgnoreCase);

        // Pattern 1: "X to Y"
        var toPattern = new Regex(@"(.+?)\s+to\s+(.+)", RegexOptions.IgnoreCase);
        var toMatch = toPattern.Match(cleanInput);
        if (toMatch.Success && toMatch.Groups.Count >= 3)
        {
            var oldText = toMatch.Groups[1].Value.Trim();
            var newText = toMatch.Groups[2].Value.Trim();

            // Clean up common phrases
            oldText = Regex.Replace(oldText, @"^(the\s+)?(reminder\s+)?(about\s+)?", "", RegexOptions.IgnoreCase).Trim();

            if (!string.IsNullOrWhiteSpace(oldText) && !string.IsNullOrWhiteSpace(newText))
            {
                return (oldText, newText);
            }
        }

        // Pattern 2: Could not parse
        return (null, null);
    }
}
