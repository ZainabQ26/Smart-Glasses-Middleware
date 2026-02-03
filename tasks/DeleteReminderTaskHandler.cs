using SmartGlassesMiddleware.Models;
using System.Text.RegularExpressions;

namespace SmartGlassesMiddleware.Services;

public class DeleteReminderTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<DeleteReminderTaskHandler> _logger;

    public string Name => "delete-reminder";

    public DeleteReminderTaskHandler(IHistoryService historyService, ILogger<DeleteReminderTaskHandler> logger)
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
                _logger.LogWarning("Delete reminder task received empty input");
                return "I didn't catch that. Please tell me which reminder you'd like to delete.";
            }

            // Parse the input to extract the reminder identifier
            // Common patterns:
            // "delete [text]"
            // "remove [text]"
            // "delete reminder about [text]"
            // "remove my reminder to [text]"
            
            // Check if user wants to delete all reminders first (before parsing)
            if (Regex.IsMatch(input, @"\b(all|every|everything)\b.*\b(reminder|reminders)\b", RegexOptions.IgnoreCase))
            {
                return "For safety, I can't delete all reminders at once. Please delete them one at a time or be specific about which ones to delete.";
            }
            
            var searchText = ParseDeleteCommand(input);
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return "I couldn't understand which reminder you want to delete. Try saying something like 'delete my reminder about milk' or 'remove buy bread reminder'.";
            }

            // Search for reminders containing the search text
            var allReminders = await _historyService.GetHistoryByTypeAsync("reminder", 100);
            var matchingReminders = allReminders
                .Where(r => r.InputText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Timestamp)
                .ToList();

            if (!matchingReminders.Any())
            {
                return $"I couldn't find any reminders matching '{searchText}'. You can say 'show my reminders' to see what you have.";
            }

            if (matchingReminders.Count > 1)
            {
                var reminderList = string.Join("\n", matchingReminders.Take(5).Select((r, i) => 
                    $"{i + 1}. {r.InputText} (created {r.Timestamp:yyyy-MM-dd HH:mm})"));
                return $"I found {matchingReminders.Count} reminders matching '{searchText}':\n\n{reminderList}\n\nPlease be more specific about which reminder you want to delete.";
            }

            // Delete the reminder
            var reminderToDelete = matchingReminders[0];
            var deleted = await _historyService.DeleteReminderAsync(reminderToDelete.Id);

            if (!deleted)
            {
                return "Sorry, I couldn't delete that reminder. It may have already been deleted.";
            }

            _logger.LogInformation("Reminder deleted successfully via AI: {ReminderId}", reminderToDelete.Id);
            return $"Okay, I've deleted your reminder: '{reminderToDelete.InputText}'.";
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation deleting reminder");
            return $"I couldn't delete that reminder: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting reminder via AI");
            return "Sorry, something went wrong while trying to delete your reminder. Please try again.";
        }
    }

    private string ParseDeleteCommand(string input)
    {
        // Remove common prefixes
        var cleanInput = Regex.Replace(input, @"^(please\s+)?(can you\s+)?(i want to\s+)?(i'd like to\s+)?", "", RegexOptions.IgnoreCase);
        cleanInput = Regex.Replace(cleanInput, @"^(delete|remove|cancel|clear|erase)\s+(my\s+)?(reminder\s+)?(about\s+|to\s+|for\s+)?", "", RegexOptions.IgnoreCase);
        
        // Clean up trailing phrases
        cleanInput = Regex.Replace(cleanInput, @"\s+(reminder|please|thanks)$", "", RegexOptions.IgnoreCase);
        
        return cleanInput.Trim();
    }
}
