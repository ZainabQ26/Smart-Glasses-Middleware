using StackExchange.Redis;
using System.Text.Json;
using SmartGlassesMiddleware.Models;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Hubs;

namespace SmartGlassesMiddleware.Services;

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<BackgroundTaskQueue> _logger;
    private readonly IHubContext<TaskHub> _hub;

    private const string QueueKey = "background:queue";
    private const string TaskKeyPrefix = "background:task:";
    private const string PubSubChannel = "background:task:completed";
    private readonly int _retentionSeconds;

    public BackgroundTaskQueue(IConnectionMultiplexer redis, ILogger<BackgroundTaskQueue> logger, IHubContext<TaskHub> hub, IConfiguration config)
    {
        _redis = redis;
        _db = _redis.GetDatabase();
        _logger = logger;
        _hub = hub;
        _retentionSeconds = config.GetValue<int?>("BackgroundTasks:RetentionSeconds") ?? 60;
    }

    public async Task EnqueueAsync(BackgroundTaskEntry entry)
    {
        var key = TaskKeyPrefix + entry.Id.ToString();
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await _db.StringSetAsync(key, json);
        await _db.ListRightPushAsync(QueueKey, entry.Id.ToString());
    }

    public async ValueTask<BackgroundTaskEntry?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var idValue = await _db.ListLeftPopAsync(QueueKey);
            if (idValue.HasValue)
            {
                if (Guid.TryParse(idValue.ToString(), out var id))
                {
                    var key = TaskKeyPrefix + id.ToString();
                    var json = await _db.StringGetAsync(key);
                    if (json.HasValue)
                    {
                        try
                        {
                            var entry = JsonSerializer.Deserialize<BackgroundTaskEntry>(json.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            return entry;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deserialize background task {Id}", id);
                            continue;
                        }
                    }
                }
            }

            try
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    public BackgroundTaskEntry? Get(Guid id)
    {
        var key = TaskKeyPrefix + id.ToString();
        var json = _db.StringGet(key);
        if (!json.HasValue) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<BackgroundTaskEntry>(json.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize background task {Id}", id);
            return null;
        }
    }

    public async Task UpdateAsync(BackgroundTaskEntry entry)
    {
        var key = TaskKeyPrefix + entry.Id.ToString();
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await _db.StringSetAsync(key, json);

        if (entry.Status == BackgroundTaskStatus.Completed || entry.Status == BackgroundTaskStatus.Failed)
        {
            // Publish a completion notification via Redis Pub/Sub
            var payload = new
            {
                id = entry.Id,
                status = entry.Status.ToString(),
                result = entry.Result,
                error = entry.Error
            };
            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            try
            {
                var channel = new RedisChannel(PubSubChannel, RedisChannel.PatternMode.Literal);
                await _redis.GetSubscriber().PublishAsync(channel, payloadJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish Redis notification for task {Id}", entry.Id);
            }

            // Also push via SignalR to connected clients
            try
            {
                await _hub.Clients.All.SendAsync("TaskCompleted", payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SignalR notification for task {Id}", entry.Id);
            }

            // Set a short TTL for the stored task so clients can poll if they miss notification
            try
            {
                await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(_retentionSeconds));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set TTL for task key {Key}", key);
            }
        }
    }
}
