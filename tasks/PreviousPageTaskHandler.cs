using SmartGlassesMiddleware.Services;

public class PreviousPageTaskHandler : ITaskHandler
{
    public string Name => "previous-page";

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PREVIOUS_PDF_PAGE__");
    }
}
