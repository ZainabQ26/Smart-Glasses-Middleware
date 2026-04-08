using SmartGlassesMiddleware.Services;

public class PreviousPageTaskHandler : ITaskHandler
{
    public string Name => "previous-page";

    public IReadOnlyList<string> Keywords => new[]
    {
        "previous page",
        "página anterior",
        "prev page",
        "pág anterior",
        "go back page",
        "retroceder página",
        "page up",
        "back page",
        "página atrás",
        "go to previous page",
        "ir a la página anterior"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PREVIOUS_PDF_PAGE__");
    }
}
