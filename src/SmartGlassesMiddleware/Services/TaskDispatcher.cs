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
                return handlers.Select(h => h.Name.ToLowerInvariant()).ToList();
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

        var handler = handlers.FirstOrDefault(h => string.Equals(h.Name, key, StringComparison.OrdinalIgnoreCase));
        if (handler == null) return (false, null);

        var resp = await handler.HandleAsync(input);
        return (true, resp);
    }
}
