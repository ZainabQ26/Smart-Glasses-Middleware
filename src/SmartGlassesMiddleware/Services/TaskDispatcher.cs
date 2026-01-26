using Microsoft.Extensions.Configuration;

namespace SmartGlassesMiddleware.Services;

public interface ITaskDispatcher
{
    IReadOnlyList<string> KnownTasks { get; }
    Task<(bool handled, string? response)> TryHandleAsync(string taskName, string input);
}

public class TaskDispatcher : ITaskDispatcher
{
    private readonly Dictionary<string, ITaskHandler> _handlers;

    public IReadOnlyList<string> KnownTasks => _handlers.Keys.ToList();

    public TaskDispatcher(IConfiguration configuration, IEnumerable<ITaskHandler> handlers)
    {
        var handlerMap = handlers?.ToDictionary(h => h.Name.ToLowerInvariant())
                         ?? new Dictionary<string, ITaskHandler>();

        // If configuration provides AI:KnownTasks, use that list to enable handlers
        var configured = configuration.GetSection("AI:KnownTasks").Get<string[]>() ?? Array.Empty<string>();
        if (configured.Length > 0)
        {
            var allowed = new HashSet<string>(configured.Select(s => s.ToLowerInvariant()));
            _handlers = handlerMap.Where(kv => allowed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        else
        {
            _handlers = handlerMap;
        }
    }

    public async Task<(bool handled, string? response)> TryHandleAsync(string taskName, string input)
    {
        if (string.IsNullOrWhiteSpace(taskName)) return (false, null);
        var key = taskName.Trim().ToLowerInvariant();
        if (!_handlers.TryGetValue(key, out var handler)) return (false, null);

        var resp = await handler.HandleAsync(input);
        return (true, resp);
    }
}
