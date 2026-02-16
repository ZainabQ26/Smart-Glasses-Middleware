using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class BackgroundTaskHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<BackgroundTaskHostedService> _logger;
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _concurrency;
    private readonly int _maxConcurrency;
    private readonly List<Task> _runningTasks = new();
    private readonly object _taskLock = new();

    public BackgroundTaskHostedService(IBackgroundTaskQueue queue, IServiceProvider services, IConfiguration configuration, ILogger<BackgroundTaskHostedService> logger)
    {
        _queue = queue;
        _services = services;
        _logger = logger;
        _maxConcurrency = configuration.GetValue<int?>("BackgroundTasks:MaxConcurrency") ?? 2;
        _concurrency = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background task worker started (maxConcurrency={Max})", _maxConcurrency);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var entry = await _queue.DequeueAsync(stoppingToken);
                if (entry == null)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    continue;
                }

                await _concurrency.WaitAsync(stoppingToken);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Dequeued task {TaskId}, starting processing", entry.Id);
                        entry.Status = BackgroundTaskStatus.Processing;
                        entry.UpdatedAt = DateTime.UtcNow;
                        await _queue.UpdateAsync(entry);

                        using var scope = _services.CreateScope();
                        var mediaProcessor = scope.ServiceProvider.GetRequiredService<IMediaProcessor>();
                        var result = await mediaProcessor.ProcessBackgroundTaskAsync(entry, stoppingToken);
                        entry.Result = result;
                        entry.Status = BackgroundTaskStatus.Completed;
                        entry.UpdatedAt = DateTime.UtcNow;
                        await _queue.UpdateAsync(entry);
                        _logger.LogInformation("Completed task {TaskId}", entry.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background task {Id} failed", entry.Id);
                        entry.Error = ex.Message;
                        entry.Status = BackgroundTaskStatus.Failed;
                        entry.UpdatedAt = DateTime.UtcNow;
                        await _queue.UpdateAsync(entry);
                    }
                    finally
                    {
                        _concurrency.Release();
                    }
                }, stoppingToken);

                lock (_taskLock)
                {
                    _runningTasks.Add(task);
                    _runningTasks.RemoveAll(t => t.IsCompleted);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        finally
        {
            Task[] remaining;
            lock (_taskLock)
            {
                remaining = _runningTasks.ToArray();
            }

            try
            {
                if (remaining.Length > 0)
                {
                    await Task.WhenAll(remaining);
                }
            }
            catch
            {
                // Ignore task failures during shutdown
            }

            _logger.LogInformation("Background task worker stopping");
        }
    }
}
