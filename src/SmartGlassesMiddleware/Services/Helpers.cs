using Microsoft.Extensions.Primitives;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Simple HttpClientFactory wrapper for creating HttpClient instances
/// </summary>
public class HttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient();
    }
}

/// <summary>
/// Configuration wrapper for passing specific values to handlers
/// </summary>
public class ConfigurationWrapper : IConfiguration
{
    private readonly Dictionary<string, string?> _values = new();

    public ConfigurationWrapper(int maxRetries, int retryDelayMs)
    {
        _values["RAG:MaxRetries"] = maxRetries.ToString();
        _values["RAG:RetryDelayMs"] = retryDelayMs.ToString();
    }

    public string? this[string key]
    {
        get => _values.ContainsKey(key) ? _values[key] : null;
        set => _values[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
    public IChangeToken GetReloadToken() => new ConfigurationReloadToken();
    public IConfigurationSection GetSection(string key) => new ConfigurationSection(this, key);
}

public class ConfigurationSection : IConfigurationSection
{
    private readonly IConfiguration _config;
    private readonly string _path;

    public ConfigurationSection(IConfiguration config, string path)
    {
        _config = config;
        _path = path;
    }

    public string? this[string key]
    {
        get => _config[$"{_path}:{key}"];
        set => _config[$"{_path}:{key}"] = value;
    }

    public string Key => _path.Split(':').Last();
    public string Path => _path;
    public string? Value
    {
        get => _config[_path];
        set => _config[_path] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
    public IChangeToken GetReloadToken() => new ConfigurationReloadToken();
    public IConfigurationSection GetSection(string key) => new ConfigurationSection(_config, $"{_path}:{key}");
}

public class ConfigurationReloadToken : IChangeToken
{
    public bool HasChanged => false;
    public bool ActiveChangeCallbacks => false;
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new EmptyDisposable();
}

public class EmptyDisposable : IDisposable
{
    public void Dispose() { }
}
