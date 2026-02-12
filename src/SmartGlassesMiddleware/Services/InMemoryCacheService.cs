using System.Collections.Concurrent;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public class InMemoryCacheService : ICacheService
{
    private record CacheEntry(string Value, DateTime? ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry> _store = new();
    private readonly ILogger<InMemoryCacheService> _logger;

    public InMemoryCacheService(ILogger<InMemoryCacheService> logger)
    {
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        try
        {
            if (_store.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
                {
                    _store.TryRemove(key, out _);
                    return Task.FromResult<T?>(default);
                }

                var value = JsonSerializer.Deserialize<T>(entry.Value);
                return Task.FromResult(value);
            }

            return Task.FromResult<T?>(default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InMemoryCacheService GetAsync error for key {Key}", key);
            return Task.FromResult<T?>(default);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value);
            DateTime? expiresAt = null;
            if (expiry.HasValue)
            {
                expiresAt = DateTime.UtcNow.Add(expiry.Value);
            }

            var entry = new CacheEntry(serialized, expiresAt);
            _store[key] = entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InMemoryCacheService SetAsync error for key {Key}", key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        try
        {
            _store.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InMemoryCacheService RemoveAsync error for key {Key}", key);
        }

        return Task.CompletedTask;
    }
}
