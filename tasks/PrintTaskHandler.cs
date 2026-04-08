using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "print document" voice command.
/// Returns an action token that tells the frontend to trigger the browser's
/// print dialog for the currently open PDF document.
/// </summary>
public class PrintTaskHandler : ITaskHandler
{
    public string Name => "print";

    public IReadOnlyList<string> Keywords =>
    new[]
    {
        "print",
        "imprimir",
        "print document",
        "imprimir documento",
        "print the document",
        "imprimir el documento",
        "print pdf",
        "imprimir pdf",
        "print the pdf",
        "imprimir el pdf",
        "print page",
        "imprimir página",
        "print this",
        "imprimir esto"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PRINT_DOCUMENT__");
    }
}
