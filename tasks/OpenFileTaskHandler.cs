using SmartGlassesMiddleware.Services;

public class OpenFileTaskHandler : ITaskHandler
{
    public string Name => "open-file";

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:SHOW_RECENT_FILES__");
    }
}
