using System.Collections.Concurrent;
using SmartGlassesMiddleware.Models;
using Microsoft.Extensions.Logging;
namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Test-friendly background queue which executes tasks synchronously when enqueued.
/// Use only in E2E/test environments to make processing deterministic.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class SynchronousBackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly ConcurrentDictionary<Guid, BackgroundTaskEntry> _store = new();
    private readonly ILogger<SynchronousBackgroundTaskQueue> _logger;
    private readonly IServiceProvider _services;

    public SynchronousBackgroundTaskQueue(ILogger<SynchronousBackgroundTaskQueue> logger, IServiceProvider services)
    {
        _logger = logger;
        _services = services;
    }

    public async Task EnqueueAsync(BackgroundTaskEntry entry)
    {
        _store[entry.Id] = entry;
        _logger.LogInformation("Synchronously executing background task {TaskId}", entry.Id);

        try
        {
            using var scope = _services.CreateScope();
            var mediaProcessor = scope.ServiceProvider.GetRequiredService<IMediaProcessor>();
            var result = await mediaProcessor.ProcessBackgroundTaskAsync(entry, CancellationToken.None);
            entry.Result = result;
            entry.Status = BackgroundTaskStatus.Completed;
            entry.UpdatedAt = DateTime.UtcNow;
            _store[entry.Id] = entry;
            _logger.LogInformation("Synchronous task {TaskId} completed", entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Synchronous task {TaskId} failed", entry.Id);
            entry.Error = ex.Message;
            entry.Status = BackgroundTaskStatus.Failed;
            entry.UpdatedAt = DateTime.UtcNow;
            _store[entry.Id] = entry;
        }

        return;
    }

    public async ValueTask<BackgroundTaskEntry?> DequeueAsync(CancellationToken cancellationToken)
    {
        // Since tasks are executed synchronously on enqueue, there's nothing to dequeue.
        await Task.Yield();
        return null;
    }

    public BackgroundTaskEntry? Get(Guid id)
    {
        return _store.TryGetValue(id, out var entry) ? entry : null;
    }

    public Task UpdateAsync(BackgroundTaskEntry entry)
    {
        _store[entry.Id] = entry;
        return Task.CompletedTask;
    }
}
