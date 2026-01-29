using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class BackgroundTaskHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<BackgroundTaskHostedService> _logger;
    private readonly IServiceProvider _services;

    public BackgroundTaskHostedService(IBackgroundTaskQueue queue, IServiceProvider services, ILogger<BackgroundTaskHostedService> logger)
    {
        _queue = queue;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background task worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var entry = await _queue.DequeueAsync(stoppingToken);
            if (entry == null) break;

            entry.Status = BackgroundTaskStatus.Processing;
            entry.UpdatedAt = DateTime.UtcNow;
            await _queue.UpdateAsync(entry);

            try
            {
                using var scope = _services.CreateScope();
                var mediaProcessor = scope.ServiceProvider.GetRequiredService<IMediaProcessor>();
                var result = await mediaProcessor.ProcessBackgroundTaskAsync(entry, stoppingToken);
                entry.Result = result;
                entry.Status = BackgroundTaskStatus.Completed;
                entry.UpdatedAt = DateTime.UtcNow;
                await _queue.UpdateAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background task {Id} failed", entry.Id);
                entry.Error = ex.Message;
                entry.Status = BackgroundTaskStatus.Failed;
                entry.UpdatedAt = DateTime.UtcNow;
                await _queue.UpdateAsync(entry);
            }
        }
        _logger.LogInformation("Background task worker stopping");
    }
}
