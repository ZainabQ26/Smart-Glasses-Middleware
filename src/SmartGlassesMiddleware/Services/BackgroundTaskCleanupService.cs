using StackExchange.Redis;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Background service that periodically cleans up expired background task entries from Redis
/// </summary>
public class BackgroundTaskCleanupService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<BackgroundTaskCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval;
    private const string TaskKeyPrefix = "background:task:";

    public BackgroundTaskCleanupService(
        IConnectionMultiplexer redis,
        ILogger<BackgroundTaskCleanupService> logger,
        IConfiguration configuration)
    {
        _redis = redis;
        _logger = logger;
        
        // Run cleanup every 5 minutes by default
        var intervalMinutes = configuration.GetValue<int?>("BackgroundTasks:CleanupIntervalMinutes") ?? 5;
        _cleanupInterval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background task cleanup service started. Cleanup interval: {Interval}", _cleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
                await CleanupExpiredTasksAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during background task cleanup");
            }
        }

        _logger.LogInformation("Background task cleanup service stopped.");
    }

    private async Task CleanupExpiredTasksAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var server = _redis.GetServers().FirstOrDefault();
            
            if (server == null)
            {
                _logger.LogWarning("No Redis server available for cleanup");
                return;
            }

            // Note: Cleanup is handled automatically by Redis TTL (set in BackgroundTaskQueue.UpdateAsync).
            // This service mainly monitors and logs the state of background tasks.
            // Redis automatically removes keys with expired TTL, so manual deletion is not needed.
            var monitoredCount = 0;
            var pattern = TaskKeyPrefix + "*";
            
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                monitoredCount++;
                var ttl = await db.KeyTimeToLiveAsync(key);
                
                // If key has no TTL (meaning it won't expire), log a warning
                // TTL is null when key doesn't exist or has no expiration
                if (ttl == null)
                {
                    // This shouldn't happen for completed tasks as they should have TTL set
                    _logger.LogWarning("Background task key {Key} has no TTL set and will not expire", key.ToString());
                }
            }

            if (monitoredCount > 0)
            {
                _logger.LogDebug("Monitored {Count} background task keys in Redis", monitoredCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring background tasks");
        }
    }
}
