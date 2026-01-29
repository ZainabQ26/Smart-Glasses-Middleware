using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using System.Text.Json;
using SmartGlassesMiddleware.Hubs;

namespace SmartGlassesMiddleware.Services;

public class RedisPubSubBridge : IHostedService, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<TaskHub> _hub;
    private readonly ILogger<RedisPubSubBridge> _logger;
    private readonly RedisPubSubBridgeStatus _status;
    private ISubscriber? _subscriber;
    private const string PubSubChannel = "background:task:completed";

    public RedisPubSubBridge(IConnectionMultiplexer redis, IHubContext<TaskHub> hub, ILogger<RedisPubSubBridge> logger, RedisPubSubBridgeStatus status)
    {
        _redis = redis;
        _hub = hub;
        _logger = logger;
        _status = status;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriber = _redis.GetSubscriber();
        _subscriber.Subscribe(new RedisChannel(PubSubChannel, RedisChannel.PatternMode.Literal), async (ch, value) =>
        {
            try
            {
                var payload = JsonSerializer.Deserialize<object>(value.ToString());
                await _hub.Clients.All.SendAsync("TaskCompleted", payload);
                _status.IsSubscribed = true;
                _status.LastError = null;
                _status.LastUpdatedUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to forward pubsub message to SignalR");
                _status.IsSubscribed = false;
                _status.LastError = ex.Message;
                _status.LastUpdatedUtc = DateTime.UtcNow;
            }
        });

        _logger.LogInformation("RedisPubSubBridge subscribed to {Channel}", PubSubChannel);
        _status.IsSubscribed = true;
        _status.LastError = null;
        _status.LastUpdatedUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _subscriber?.Unsubscribe(new RedisChannel(PubSubChannel, RedisChannel.PatternMode.Literal));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unsubscribing RedisPubSubBridge");
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // nothing to dispose explicitly (IConnectionMultiplexer is owned elsewhere)
    }
}
