namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Background service that syncs FDA abbreviations from openFDA weekly.
/// On startup, checks if a sync is needed (based on last update age).
/// Since ECS containers spin up and down, this ensures syncs happen on schedule
/// regardless of restarts.
/// </summary>
public class AbbreviationSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AbbreviationSyncBackgroundService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromDays(7); // Weekly
    private DateTime _nextSyncTime;

    public AbbreviationSyncBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<AbbreviationSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _nextSyncTime = DateTime.UtcNow; // Will be calculated in StartAsync
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Abbreviation sync background service starting...");
        
        // Check if sync is needed on boot
        var lastSyncTime = await GetLastSyncTimeAsync();
        var timeSinceLastSync = DateTime.UtcNow - lastSyncTime;

        if (timeSinceLastSync >= _syncInterval)
        {
            _logger.LogInformation("Last sync was {Days} days ago. Syncing now...", timeSinceLastSync.TotalDays);
            await SyncAbbreviationsAsync(cancellationToken);
            _nextSyncTime = DateTime.UtcNow.Add(_syncInterval);
        }
        else
        {
            // Schedule next sync based on when the last one happened
            _nextSyncTime = lastSyncTime.Add(_syncInterval);
            _logger.LogInformation("Last sync was {Days} days ago. Next sync scheduled for {Time} UTC", 
                timeSinceLastSync.TotalDays, _nextSyncTime);
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Abbreviation sync background service running. Next sync scheduled for {Time} UTC", _nextSyncTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var timeUntilSync = _nextSyncTime - now;

            // If it's time to sync, do it now; otherwise wait until then
            if (timeUntilSync <= TimeSpan.Zero)
            {
                _logger.LogInformation("Starting weekly FDA abbreviation sync...");
                await SyncAbbreviationsAsync(stoppingToken);
                _nextSyncTime = DateTime.UtcNow.Add(_syncInterval);
                _logger.LogInformation("Sync complete. Next sync scheduled for {Time} UTC", _nextSyncTime);
            }

            // Wait for the next sync or cancellation (check every minute)
            var delayTime = timeUntilSync > TimeSpan.FromMinutes(1) 
                ? TimeSpan.FromMinutes(1) 
                : timeUntilSync;

            try
            {
                await Task.Delay(delayTime, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Abbreviation sync background service is shutting down");
                break;
            }
        }
    }

    private async Task<DateTime> GetLastSyncTimeAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SmartGlassesMiddleware.Data.ApplicationDbContext>();

            // Get the most recent UpdatedAt timestamp from abbreviations
            var lastUpdate = await context.Abbreviations
                .OrderByDescending(a => a.UpdatedAt)
                .Select(a => a.UpdatedAt)
                .FirstOrDefaultAsync();

            // If no abbreviations exist yet, return distant past so we sync immediately on first boot
            if (lastUpdate == default)
            {
                _logger.LogInformation("No abbreviations found in database. First sync will run immediately.");
                return DateTime.UnixEpoch; // Ancient date ensures immediate sync
            }

            return lastUpdate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch last sync time from database. Assuming immediate sync needed.");
            return DateTime.UnixEpoch;
        }
    }

    private async Task SyncAbbreviationsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var abbreviationService = scope.ServiceProvider.GetRequiredService<IAbbreviationService>();

            var addedCount = await abbreviationService.SyncFromDailyMedAsync(stoppingToken);
            
            _logger.LogInformation("FDA abbreviation sync completed successfully. Added/updated {Count} abbreviations", addedCount);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Abbreviation sync was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FDA abbreviation sync failed (will retry in 7 days)");
        }
    }
}

