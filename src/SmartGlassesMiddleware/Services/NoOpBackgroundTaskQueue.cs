using System.Collections.Concurrent;
using SmartGlassesMiddleware.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace SmartGlassesMiddleware.Services;

public class NoOpBackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly ConcurrentQueue<BackgroundTaskEntry> _queue = new();
    private readonly ConcurrentDictionary<Guid, BackgroundTaskEntry> _store = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ILogger<NoOpBackgroundTaskQueue> _logger;
    private readonly int _maxQueueSize;
    private readonly int _retentionSeconds;

    public NoOpBackgroundTaskQueue(ILogger<NoOpBackgroundTaskQueue> logger, IConfiguration config)
    {
        _logger = logger;
        _maxQueueSize = config.GetValue<int?>("BackgroundTasks:MaxQueueSize") ?? 500;
        _retentionSeconds = config.GetValue<int?>("BackgroundTasks:RetentionSeconds") ?? 60;
    }

    public Task EnqueueAsync(BackgroundTaskEntry entry)
    {
        PruneStore();

        // Mirror Redis queue behavior: reject when queue reaches configured max size.
        if (_queue.Count >= _maxQueueSize)
        {
            _logger.LogWarning("In-memory background task queue full ({QueueLen}/{MaxSize}), rejecting task {TaskId}", _queue.Count, _maxQueueSize, entry.Id);
            throw new InvalidOperationException($"Background task queue is full (size: {_queue.Count}). Please try again later.");
        }

        // If the store is still saturated after pruning, reject to avoid unbounded growth.
        if (_store.Count >= _maxQueueSize)
        {
            _logger.LogWarning("In-memory background task store full ({StoreLen}/{MaxSize}), rejecting task {TaskId}", _store.Count, _maxQueueSize, entry.Id);
            throw new InvalidOperationException($"Background task store is full (size: {_store.Count}). Please try again later.");
        }

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
        if (!_store.TryGetValue(id, out var entry))
        {
            return null;
        }

        if (IsTerminal(entry) && IsExpired(entry, DateTime.UtcNow))
        {
            _store.TryRemove(id, out _);
            return null;
        }

        return entry;
    }

    public Task UpdateAsync(BackgroundTaskEntry entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        _store[entry.Id] = entry;
        PruneStore();
        return Task.CompletedTask;
    }

    private void PruneStore()
    {
        if (_store.IsEmpty)
        {
            return;
        }

        var now = DateTime.UtcNow;

        // First pass: remove terminal tasks that have exceeded retention TTL.
        foreach (var kvp in _store)
        {
            if (IsTerminal(kvp.Value) && IsExpired(kvp.Value, now))
            {
                _store.TryRemove(kvp.Key, out _);
            }
        }

        // Second pass: if still above cap, evict oldest terminal tasks.
        if (_store.Count <= _maxQueueSize)
        {
            return;
        }

        var terminalCandidates = _store
            .Where(kvp => IsTerminal(kvp.Value))
            .OrderBy(kvp => kvp.Value.UpdatedAt ?? kvp.Value.CreatedAt)
            .ToList();

        foreach (var candidate in terminalCandidates)
        {
            if (_store.Count <= _maxQueueSize)
            {
                break;
            }
            _store.TryRemove(candidate.Key, out _);
        }
    }

    private static bool IsTerminal(BackgroundTaskEntry entry)
    {
        return entry.Status == BackgroundTaskStatus.Completed || entry.Status == BackgroundTaskStatus.Failed;
    }

    private bool IsExpired(BackgroundTaskEntry entry, DateTime nowUtc)
    {
        var touchedAt = entry.UpdatedAt ?? entry.CreatedAt;
        return touchedAt <= nowUtc.AddSeconds(-_retentionSeconds);
    }
}
