using SmartGlassesMiddleware.Services;

public class NextPageTaskHandler : ITaskHandler
{
    public string Name => "next-page";

    public IReadOnlyList<string> Keywords => new[]
    {
        "next page",
        "siguiente página",
        "go to next page",
        "ir a la siguiente página",
        "page down",
        "avanzar página",
        "forward page",
        "página adelante",
        "page forward"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:NEXT_PDF_PAGE__");
    }
}
