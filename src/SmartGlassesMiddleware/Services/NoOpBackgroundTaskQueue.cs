using System.Collections.Concurrent;
using SmartGlassesMiddleware.Models;
using Microsoft.Extensions.Logging;

namespace SmartGlassesMiddleware.Services;

public class NoOpBackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly ConcurrentQueue<BackgroundTaskEntry> _queue = new();
    private readonly ConcurrentDictionary<Guid, BackgroundTaskEntry> _store = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ILogger<NoOpBackgroundTaskQueue> _logger;

    public NoOpBackgroundTaskQueue(ILogger<NoOpBackgroundTaskQueue> logger)
    {
        _logger = logger;
    }

    public Task EnqueueAsync(BackgroundTaskEntry entry)
    {
        _store[entry.Id] = entry;
        _queue.Enqueue(entry);
        _signal.Release();
        // Log removed
        return Task.CompletedTask;
    }

    public async ValueTask<BackgroundTaskEntry?> DequeueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _signal.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (_queue.TryDequeue(out var item))
        {
            return item;
        }

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
