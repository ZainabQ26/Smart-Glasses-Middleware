using System.Globalization;

namespace SmartGlassesMiddleware.Services;

public class TimeTaskHandler : ITaskHandler
{
    public string Name => "time";

    public Task<string> HandleAsync(string input)
    {
        var now = DateTime.Now;
        var response = $"The current time is {now.ToString("T", CultureInfo.CurrentCulture)} on {now.ToString("d", CultureInfo.CurrentCulture)}.";
        return Task.FromResult(response);
    }
}
