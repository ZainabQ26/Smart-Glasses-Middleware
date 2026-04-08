using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "print highlighted pages" voice command.
/// Returns an action token instructing the frontend to print only pages that
/// contain at least one active highlight.
/// </summary>
public class PrintHighlightedPagesTaskHandler : ITaskHandler
{
    public string Name => "print-highlighted-pages";

    public IReadOnlyList<string> Keywords =>
    new[]
    {
        "print highlighted pages",
        "imprimir páginas resaltadas",
        "print highlighted page",
        "print only highlighted pages",
        "imprimir solo páginas resaltadas",
        "print only highlighted page",
        "print highlights only",
        "imprimir solo resaltados",
        "print just highlighted pages",
        "print pages with highlights",
        "imprimir páginas con resaltados",
        "print highlighted",
        "imprimir resaltados",
        "stitch highlighted pages",
        "unir páginas resaltadas",
        "stitch together highlighted pages"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PRINT_HIGHLIGHTED_PAGES__");
    }
}
