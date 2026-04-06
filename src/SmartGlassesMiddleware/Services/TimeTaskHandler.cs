using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Services;

public class TimeTaskHandler : ITaskHandler
{
    public string Name => "time";

    public IReadOnlyList<string> Keywords => new[] { "what time", "current time" };

    public Task<string> HandleAsync(string input)
    {
        var currentTime = DateTime.Now.ToString("h:mm tt");
        return Task.FromResult($"The current time is {currentTime}.");
    }
}
