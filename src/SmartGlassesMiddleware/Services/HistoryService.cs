using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Services;

public interface IHistoryService
{
    Task<RequestHistory> SaveRequestAsync(RequestHistory history);
    Task<List<RequestHistory>> GetRecentHistoryAsync(int count = 10);
    Task<RequestHistory?> GetHistoryByIdAsync(Guid id);
}

public class HistoryService : IHistoryService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(ApplicationDbContext context, ILogger<HistoryService> logger)
    {
        _context = context;
        _logger = logger;
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
}
