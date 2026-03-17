using SmartGlassesMiddleware.Services;

public class PreviousPageTaskHandler : ITaskHandler
{
    public string Name => "previous-page";

    public IReadOnlyList<string> Keywords => new[]
    {
        "previous page", "prev page", "go back page", "page up", "back page", "go to previous page"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PREVIOUS_PDF_PAGE__");
    }
}
