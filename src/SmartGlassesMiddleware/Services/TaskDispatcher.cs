using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace SmartGlassesMiddleware.Services;

public interface ITaskDispatcher
{
    IReadOnlyList<string> KnownTasks { get; }
    Task<(bool handled, string? response)> TryHandleAsync(string taskName, string input);
    IReadOnlyDictionary<string, string> GetHandlerTypeMap();
    /// <summary>
    /// Match the user input against each handler's Keywords list.
    /// Returns the first matching task name, or null if no keyword matched.
    /// </summary>
    string? TryMatchByKeyword(string input);
}

public class TaskDispatcher : ITaskDispatcher
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskDispatcher> _logger;
    private IReadOnlyDictionary<string, Type>? _handlerTypeMap;

    private IReadOnlyDictionary<string, Type> HandlerTypeMap
    {
        get
        {
            if (_handlerTypeMap != null) return _handlerTypeMap;

            var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (!typeof(ITaskHandler).IsAssignableFrom(type) || !type.IsClass || type.IsAbstract) continue;

                    var taskName = InferTaskNameFromType(type);
                    if (string.IsNullOrWhiteSpace(taskName)) continue;

                    if (!map.ContainsKey(taskName))
                    {
                        map[taskName] = type;
                    }
                }
            }

            _handlerTypeMap = map;
            return _handlerTypeMap;
        }
    }

    public IReadOnlyList<string> KnownTasks
    {
        get
        {
            var discovered = HandlerTypeMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var configured = _configuration.GetSection("AI:KnownTasks").GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            if (configured.Count > 0)
            {
                var missing = configured.Where(t => !discovered.Contains(t)).ToList();
                if (missing.Count > 0)
                {
                    _logger.LogWarning("KnownTasks contains entries without handlers: {Missing}", string.Join(", ", missing));
                }

                return configured.Where(t => discovered.Contains(t)).ToList();
            }

            return discovered.OrderBy(x => x).ToList();
        }
    }

    public TaskDispatcher(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<TaskDispatcher> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<(bool handled, string? response)> TryHandleAsync(string taskName, string input)
    {
        if (string.IsNullOrWhiteSpace(taskName)) return (false, null);
        var key = taskName.Trim().ToLowerInvariant();

        var allowed = KnownTasks ?? Array.Empty<string>();
        var allowedSet = new HashSet<string>(
            allowed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.ToLowerInvariant()));
        if (allowedSet.Count > 0 && !allowedSet.Contains(key)) return (false, null);

        if (!HandlerTypeMap.TryGetValue(key, out var handlerType)) return (false, null);

        using var scope = _serviceProvider.CreateScope();

        ITaskHandler handler;
        try
        {
            handler = (ITaskHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, handlerType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to activate handler type {HandlerType} for task {Task}", handlerType.FullName, key);
            return (false, null);
        }

        var resp = await handler.HandleAsync(input);
        return (true, resp);
    }

    public IReadOnlyDictionary<string, string> GetHandlerTypeMap()
    {
        return HandlerTypeMap
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.FullName ?? kvp.Value.Name,
                StringComparer.OrdinalIgnoreCase);
    }

    public string? TryMatchByKeyword(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var normalizedInput = input.ToLowerInvariant();
        var allowed = new HashSet<string>(
            (KnownTasks ?? Array.Empty<string>()).Select(t => t.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        // Instantiate handlers briefly to read their keywords.
        // Use a temporary scope per handler type.
        using var scope = _serviceProvider.CreateScope();
        string? bestMatch = null;
        int bestLength = 0;

        foreach (var (taskName, handlerType) in HandlerTypeMap)
        {
            if (allowed.Count > 0 && !allowed.Contains(taskName)) continue;

            ITaskHandler handler;
            try
            {
                handler = (ITaskHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, handlerType);
            }
            catch
            {
                continue;
            }

            foreach (var keyword in handler.Keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                var lowerKeyword = keyword.ToLowerInvariant().Trim();
                // Use word-boundary matching so "print" doesn't match "fingerprint".
                // A keyword matches when it appears in the input surrounded by
                // non-alphanumeric characters (or at the start/end of the string).
                if (ContainsWholePhrase(normalizedInput, lowerKeyword))
                {
                    // Prefer the longest matching keyword to avoid false positives.
                    if (lowerKeyword.Length > bestLength)
                    {
                        bestLength = lowerKeyword.Length;
                        bestMatch = taskName;
                    }
                }
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Returns true when <paramref name="phrase"/> appears in <paramref name="text"/>
    /// as a whole word or phrase, i.e. not embedded inside another word.
    /// Both arguments are expected to be already lowercased.
    /// </summary>
    private static bool ContainsWholePhrase(string text, string phrase)
    {
        int idx = 0;
        while (true)
        {
            idx = text.IndexOf(phrase, idx, StringComparison.Ordinal);
            if (idx < 0) return false;

            int end = idx + phrase.Length;
            bool leftOk  = idx == 0         || !char.IsLetterOrDigit(text[idx - 1]);
            bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);

            if (leftOk && rightOk) return true;
            idx = end; // advance past this occurrence and try again
        }
    }

    private static string InferTaskNameFromType(Type handlerType)
    {
        var name = handlerType.Name;
        if (name.EndsWith("TaskHandler", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - "TaskHandler".Length);
        }

        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (i > 0 && char.IsUpper(current))
            {
                var previous = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    sb.Append('-');
                }
            }

            sb.Append(char.ToLowerInvariant(current));
        }

        return sb.ToString();
    }
}
