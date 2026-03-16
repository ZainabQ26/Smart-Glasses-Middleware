using System.Globalization;

namespace SmartGlassesMiddleware.Services;

public class TimeTaskHandler : ITaskHandler
{
    public string Name => "time";

    public IReadOnlyList<string> Keywords => new[]
    {
        "what time", "current time", "what's the time", "what is the time", "what day",
        "what's today", "what is today", "date and time", "tell me the time"
    };

    public Task<string> HandleAsync(string input)
    {
        var now = DateTime.Now;
        var response = $"The current time is {now.ToString("T", CultureInfo.CurrentCulture)} on {now.ToString("d", CultureInfo.CurrentCulture)}.";
        return Task.FromResult(response);
    }
}
