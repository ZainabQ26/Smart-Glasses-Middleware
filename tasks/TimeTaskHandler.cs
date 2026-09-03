using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tasks;

public class TimeTaskHandler : ITaskHandler
{
    public string Name => "time";

    public IReadOnlyList<string> Keywords => new[]
    {
        "what time is it",
        "what's the time",
        "current time",
        "tell me the time",
        "what time",
        "qué hora es",
        "hora actual"
    };

    public Task<string> HandleAsync(string input)
    {
        var now = DateTime.Now.ToString("h:mm tt");
        return Task.FromResult($"The current time is {now}.");
    }
}
