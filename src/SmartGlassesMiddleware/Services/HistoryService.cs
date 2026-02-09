using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Services;

public interface IHistoryService
{
    Task<RequestHistory> SaveRequestAsync(RequestHistory history);
    Task<List<RequestHistory>> GetRecentHistoryAsync(int count = 10);
    Task<RequestHistory?> GetHistoryByIdAsync(Guid id);
    Task<List<RequestHistory>> GetHistoryByTypeAsync(string requestType, int count = 100);
    Task<RequestHistory?> UpdateReminderAsync(Guid id, string newText);
    Task<bool> DeleteReminderAsync(Guid id);
}

public class HistoryService : IHistoryService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<HistoryService> _logger;
    private readonly IRequestMonitoringService? _monitoringService;

    public HistoryService(ApplicationDbContext context, ILogger<HistoryService> logger, IRequestMonitoringService? monitoringService = null)
    {
        _context = context;
        _logger = logger;
        _monitoringService = monitoringService;
    }

    public async Task<RequestHistory> SaveRequestAsync(RequestHistory history)
    {
        try
        {
            history.Id = Guid.NewGuid();
            history.Timestamp = DateTime.UtcNow;

            _context.RequestHistories.Add(history);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Saved request history with ID: {Id}", history.Id);

            // Notify monitoring service about this saved history so live UI can display it
            try
            {
                if (_monitoringService != null)
                {
                    // If caller already set a CorrelationId (e.g. document query started earlier), use it
                    var desc = !string.IsNullOrWhiteSpace(history.InputText) ? history.InputText : (history.DetectedTask ?? history.RequestType ?? "Request");
                    string monId;
                    if (!string.IsNullOrWhiteSpace(history.CorrelationId))
                    {
                        monId = history.CorrelationId!;
                    }
                    else
                    {
                        monId = await _monitoringService.StartRequest(history.RequestType ?? "History", desc);
                    }

                    await _monitoringService.UpdateRequestStatus(monId, "Completed", details: history.Response);
                    await _monitoringService.CompleteRequest(monId, true, result: history.Id.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify monitoring service for history ID: {Id}", history.Id);
            }

            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving request history");
            throw;
        }
    }

    public async Task<List<RequestHistory>> GetRecentHistoryAsync(int count = 10)
    {
        try
        {
            return await _context.RequestHistories
                .OrderByDescending(h => h.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent history");
            throw;
        }
    }

    public async Task<RequestHistory?> GetHistoryByIdAsync(Guid id)
    {
        try
        {
            return await _context.RequestHistories.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history by ID: {Id}", id);
            throw;
        }
    }

    public async Task<List<RequestHistory>> GetHistoryByTypeAsync(string requestType, int count = 100)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(requestType))
            {
                _logger.LogWarning("GetHistoryByTypeAsync called with empty request type");
                return new List<RequestHistory>();
            }

            return await _context.RequestHistories
                .Where(h => h.RequestType == requestType)
                .OrderByDescending(h => h.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history by type: {Type}", requestType);
            throw;
        }
    }

    public async Task<RequestHistory?> UpdateReminderAsync(Guid id, string newText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newText))
            {
                _logger.LogWarning("UpdateReminderAsync called with empty text for ID: {Id}", id);
                throw new ArgumentException("Reminder text cannot be empty", nameof(newText));
            }

            if (newText.Length > 1000)
            {
                _logger.LogWarning("UpdateReminderAsync called with text exceeding length limit for ID: {Id}", id);
                throw new ArgumentException("Reminder text cannot exceed 1000 characters", nameof(newText));
            }

            var reminder = await _context.RequestHistories.FindAsync(id);

            if (reminder == null)
            {
                _logger.LogWarning("UpdateReminderAsync: Reminder not found with ID: {Id}", id);
                return null;
            }

            if (reminder.RequestType != "reminder")
            {
                _logger.LogWarning("UpdateReminderAsync: Attempted to update non-reminder entry with ID: {Id}", id);
                throw new InvalidOperationException("Can only update reminder-type history entries");
            }

            // Update the reminder text
            reminder.InputText = newText.Trim();
            reminder.Response = $"Reminder updated: {reminder.InputText}";

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated reminder with ID: {Id}", id);
            return reminder;
        }
        catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Error updating reminder with ID: {Id}", id);
            throw;
        }
    }

    public async Task<bool> DeleteReminderAsync(Guid id)
    {
        try
        {
            var reminder = await _context.RequestHistories.FindAsync(id);

            if (reminder == null)
            {
                _logger.LogWarning("DeleteReminderAsync: Reminder not found with ID: {Id}", id);
                return false;
            }

            if (reminder.RequestType != "reminder")
            {
                _logger.LogWarning("DeleteReminderAsync: Attempted to delete non-reminder entry with ID: {Id}", id);
                throw new InvalidOperationException("Can only delete reminder-type history entries");
            }

            _context.RequestHistories.Remove(reminder);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted reminder with ID: {Id}", id);
            return true;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Error deleting reminder with ID: {Id}", id);
            throw;
        }
    }
}
