using SmartGlassesMiddleware.Services;

public class NextPageTaskHandler : ITaskHandler
{
    public string Name => "next-page";

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:NEXT_PDF_PAGE__");
    }
}
