using StackExchange.Redis;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task RemoveAsync(string key);
}

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly TimeSpan _operationTimeout;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger, IConfiguration configuration)
    {
        _redis = redis;
        _logger = logger;
        var timeoutMs = configuration.GetValue<int?>("Cache:RedisOperationTimeoutMs") ?? 300;
        _operationTimeout = TimeSpan.FromMilliseconds(Math.Max(50, timeoutMs));
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var getTask = db.StringGetAsync(key);
            var completed = await Task.WhenAny(getTask, Task.Delay(_operationTimeout));
            if (completed != getTask)
            {
                _logger.LogWarning("Redis GET timed out for key: {Key} after {TimeoutMs}ms", key, _operationTimeout.TotalMilliseconds);
                return default;
            }

            var value = await getTask;

            if (value.IsNullOrEmpty)
                return default;

            return JsonSerializer.Deserialize<T>(value.ToString()!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting value from Redis cache for key: {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var db = _redis.GetDatabase();
            var serializedValue = JsonSerializer.Serialize(value);
            var setTask = db.StringSetAsync(key, serializedValue, expiry);
            var completed = await Task.WhenAny(setTask, Task.Delay(_operationTimeout));
            if (completed != setTask)
            {
                _logger.LogWarning("Redis SET timed out for key: {Key} after {TimeoutMs}ms", key, _operationTimeout.TotalMilliseconds);
                return;
            }

            await setTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting value in Redis cache for key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var removeTask = db.KeyDeleteAsync(key);
            var completed = await Task.WhenAny(removeTask, Task.Delay(_operationTimeout));
            if (completed != removeTask)
            {
                _logger.LogWarning("Redis DELETE timed out for key: {Key} after {TimeoutMs}ms", key, _operationTimeout.TotalMilliseconds);
                return;
            }

            await removeTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing value from Redis cache for key: {Key}", key);
        }
    }
}
