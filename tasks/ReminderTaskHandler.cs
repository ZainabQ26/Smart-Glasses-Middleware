using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class ReminderTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;

    public string Name => "reminder";

    public ReminderTaskHandler(IHistoryService historyService)
    {
        _historyService = historyService;
    }

    public async Task<string> HandleAsync(string input)
    {
        // Very small, naive reminder persistence: store the raw input as a RequestHistory entry.
        var history = new RequestHistory
        {
            RequestType = "reminder",
            InputText = input,
            DetectedTask = Name,
            Response = "Reminder saved."
        };

        await _historyService.SaveRequestAsync(history);

        return "Okay — I've saved that reminder.";
    }
}
