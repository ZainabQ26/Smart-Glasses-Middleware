using Microsoft.Extensions.Configuration;

namespace SmartGlassesMiddleware.Services;

public interface ITaskDispatcher
{
    IReadOnlyList<string> KnownTasks { get; }
    Task<(bool handled, string? response)> TryHandleAsync(string taskName, string input);
}

public class TaskDispatcher : ITaskDispatcher
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public IReadOnlyList<string> KnownTasks
    {
        get
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var handlers = scope.ServiceProvider.GetServices<ITaskHandler>();
                var handlerNames = handlers
                    .Select(h => (h.Name ?? string.Empty).ToLowerInvariant())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                // If configuration specifies a known tasks list, filter to that list
                try
                {
                    var configured = _configuration.GetSection("AI:KnownTasks").GetChildren()
                        .Select(c => c.Value)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToArray();
                    if (configured != null && configured.Length > 0)
                    {
                        var allowed = configured
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s!.ToLowerInvariant())
                            .ToHashSet();
                        return handlerNames.Where(n => allowed.Contains(n)).ToList();
                    }
                }
                catch
                {
                    // ignore config parse errors and fall back to all handlers
                }

                return handlerNames;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    public TaskDispatcher(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public async Task<(bool handled, string? response)> TryHandleAsync(string taskName, string input)
    {
        if (string.IsNullOrWhiteSpace(taskName)) return (false, null);
        var key = taskName.Trim().ToLowerInvariant();

        using var scope = _serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<ITaskHandler>();
        var allowed = KnownTasks ?? Array.Empty<string>();
        var allowedSet = new HashSet<string>(
            allowed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.ToLowerInvariant()));

        var handler = handlers.FirstOrDefault(h =>
        {
            var name = h.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var lowered = name.ToLowerInvariant();
            if (allowedSet.Count > 0 && !allowedSet.Contains(lowered)) return false;
            return string.Equals(lowered, key, StringComparison.OrdinalIgnoreCase);
        });
        if (handler == null) return (false, null);

        var resp = await handler.HandleAsync(input);
        return (true, resp);
    }
}
