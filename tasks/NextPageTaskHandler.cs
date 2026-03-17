using SmartGlassesMiddleware.Services;

public class NextPageTaskHandler : ITaskHandler
{
    public string Name => "next-page";

    public IReadOnlyList<string> Keywords => new[]
    {
        "next page", "go to next page", "page down", "forward page", "page forward"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:NEXT_PDF_PAGE__");
    }
}
