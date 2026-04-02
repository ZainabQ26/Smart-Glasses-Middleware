using System.Text.Json;
using SmartGlassesMiddleware.Models;
using System.Collections.Concurrent;

namespace SmartGlassesMiddleware.Services;

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly ConcurrentQueue<BackgroundTaskEntry> _queue = new();
    private readonly ConcurrentDictionary<Guid, BackgroundTaskEntry> _tasks = new();
    private readonly ILogger<BackgroundTaskQueue> _logger;
    private readonly IRequestMonitoringService? _monitoringService;
    private readonly int _retentionSeconds;
    private readonly int _maxQueueSize;

    public BackgroundTaskQueue(
        ILogger<BackgroundTaskQueue> logger,
        IConfiguration config,
        IRequestMonitoringService? monitoringService = null)
    {
        _logger = logger;
        _monitoringService = monitoringService;
        _retentionSeconds = config.GetValue<int?>("BackgroundTasks:RetentionSeconds") ?? 60;
        _maxQueueSize = config.GetValue<int?>("BackgroundTasks:MaxQueueSize") ?? 500;
    }

    public async Task EnqueueAsync(BackgroundTaskEntry entry)
    {
        if (_queue.Count >= _maxQueueSize)
        {
            _logger.LogWarning("Background task queue is full (Max: {Max}). Task {TaskId} dropped.", _maxQueueSize, entry.Id);
            return;
        }

        entry.Status = "Pending";
        _tasks[entry.Id] = entry;
        _queue.Enqueue(entry);
        
        if (_monitoringService != null)
        {
            await _monitoringService.UpdateTaskStatusAsync(entry.Id.ToString(), "Pending", entry.TaskType);
        }
    }

    public async ValueTask<BackgroundTaskEntry?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_queue.TryDequeue(out var entry))
            {
                if (_tasks.TryGetValue(entry.Id, out var existing))
                {
                    existing.Status = "Processing";
                    return existing;
                }
                return entry;
            }

            await Task.Delay(100, cancellationToken);
        }
        return null;
    }

    public BackgroundTaskEntry? Get(Guid id)
    {
        _tasks.TryGetValue(id, out var entry);
        return entry;
    }

    public async Task UpdateAsync(BackgroundTaskEntry entry)
    {
        _tasks[entry.Id] = entry;

        if (_monitoringService != null)
        {
            string? resultDataStr = null;
            if (entry.ResultData != null)
            {
                resultDataStr = JsonSerializer.Serialize(entry.ResultData);
            }
            await _monitoringService.UpdateTaskStatusAsync(
                entry.Id.ToString(),
                entry.Status,
                entry.TaskType,
                resultDataStr,
                entry.ErrorMessage
            );
        }
    }
}
