using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class DeleteReminderTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<DeleteReminderTaskHandler> _logger;

    public string Name => "delete-reminder";

    public IReadOnlyList<string> Keywords => new[] { "delete reminder" };

    public DeleteReminderTaskHandler(IHistoryService historyService, ILogger<DeleteReminderTaskHandler> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "I didn't catch which reminder to delete.";
        }

        var inputLower = input.ToLowerInvariant();
        if (inputLower.Contains("all reminders") || inputLower.Contains("every reminder"))
        {
            return "For safety, you can't delete all reminders at once. Please delete them one at a time.";
        }

        var keywordIndex = inputLower.IndexOf("delete reminder");
        string searchPhrase = inputLower;
        if (keywordIndex != -1)
        {
            searchPhrase = inputLower.Substring(keywordIndex + "delete reminder".Length).Trim();
        }

        if (string.IsNullOrWhiteSpace(searchPhrase) || searchPhrase == "delete")
        {
            return "Please be more specific about which reminder to delete.";
        }

        var reminders = await _historyService.GetHistoryByTypeAsync("reminder", 100);
        var matches = reminders.Where(r => r.InputText.ToLowerInvariant().Contains(searchPhrase)).ToList();

        if (matches.Count == 0)
        {
            return $"I couldn't find a reminder about '{searchPhrase}'. No reminder was deleted.";
        }

        if (matches.Count > 1)
        {
            return $"I found {matches.Count} reminders matching '{searchPhrase}'. Please be more specific.";
        }

        var match = matches.First();
        await _historyService.DeleteReminderAsync(match.Id);
        return $"I deleted the reminder: {match.InputText}";
    }
}
