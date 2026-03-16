using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "print document" voice command.
/// Returns an action token that tells the frontend to trigger the browser's
/// print dialog for the currently open PDF document.
/// </summary>
public class PrintTaskHandler : ITaskHandler
{
    public string Name => "print";

    public IReadOnlyList<string> Keywords => new[]
    {
        "print", "print document", "print the document", "print pdf", "print the pdf",
        "print page", "print this"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PRINT_DOCUMENT__");
    }
}
