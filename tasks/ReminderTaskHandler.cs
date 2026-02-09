using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class ReminderTaskHandler : ITaskHandler
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<ReminderTaskHandler> _logger;
    private readonly IRequestMonitoringService? _monitoringService;

    public string Name => "reminder";

    public ReminderTaskHandler(IHistoryService historyService, ILogger<ReminderTaskHandler> logger, IRequestMonitoringService? monitoringService = null)
    {
        _historyService = historyService;
        _logger = logger;
        _monitoringService = monitoringService;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            // Handle edge case: empty or whitespace input
            if (string.IsNullOrWhiteSpace(input))
            {
                _logger.LogWarning("Reminder task received empty input");
                return "I didn't catch that. Please tell me what you'd like to be reminded about.";
            }

            // Trim and validate the input
            var reminderText = input.Trim();

            // Optional: Add a reasonable length limit to prevent abuse
            if (reminderText.Length > 1000)
            {
                _logger.LogWarning("Reminder task received excessively long input: {Length} characters", reminderText.Length);
                return "That reminder is too long. Please keep it under 1000 characters.";
            }

            // Save the reminder to database
            string? monitoringRequestId = null;
            try
            {
                if (_monitoringService != null)
                {
                    monitoringRequestId = await _monitoringService.StartRequest("Reminder", reminderText.Length > 100 ? reminderText.Substring(0, 100) : reminderText);
                    await _monitoringService.UpdateRequestStatus(monitoringRequestId, "Saving reminder");
                }

                var history = new RequestHistory
                {
                    RequestType = "reminder",
                    InputText = reminderText,
                    DetectedTask = Name,
                    Response = "Reminder saved."
                };
                if (!string.IsNullOrWhiteSpace(monitoringRequestId)) history.CorrelationId = monitoringRequestId;

                var savedHistory = await _historyService.SaveRequestAsync(history);

                if (!string.IsNullOrWhiteSpace(monitoringRequestId) && _monitoringService != null)
                {
                    await _monitoringService.CompleteRequest(monitoringRequestId, true, result: savedHistory.Id.ToString());
                }

                _logger.LogInformation("Reminder saved successfully with ID: {ReminderId}", savedHistory.Id);

                return $"Okay — I've saved that reminder: \"{reminderText}\"";
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(monitoringRequestId) && _monitoringService != null)
                {
                    await _monitoringService.CompleteRequest(monitoringRequestId, false, error: ex.Message);
                }
                _logger.LogError(ex, "Error saving reminder");
                return "Sorry, I couldn't save that reminder. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving reminder");
            return "Sorry, I couldn't save that reminder. Please try again.";
        }
    }
}
