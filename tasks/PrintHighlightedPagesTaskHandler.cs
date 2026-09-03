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
        "imprimir paginas resaltadas",
        "imprimir las páginas resaltadas",
        "imprimir las paginas resaltadas",
        "imprimir páginas destacadas",
        "imprimir paginas destacadas",
        "imprimir las páginas destacadas",
        "imprimir las paginas destacadas",
        "imprimir páginas marcadas",
        "imprimir paginas marcadas",
        "imprimir las páginas marcadas",
        "imprimir las paginas marcadas",
        "imprimir páginas subrayadas",
        "imprimir paginas subrayadas",
        "print highlighted page",
        "imprimir página resaltada",
        "imprimir pagina resaltada",
        "imprimir la página resaltada",
        "imprimir la pagina resaltada",
        "print only highlighted pages",
        "imprimir solo páginas resaltadas",
        "imprimir solo paginas resaltadas",
        "imprimir solo las páginas resaltadas",
        "imprimir solo las paginas resaltadas",
        "print only highlighted page",
        "print highlights only",
        "imprimir solo resaltados",
        "imprimir solo resaltado",
        "print just highlighted pages",
        "print pages with highlights",
        "imprimir páginas con resaltados",
        "imprimir paginas con resaltados",
        "imprimir paginas con destacados",
        "print highlighted",
        "imprimir resaltados",
        "imprimir destacadas",
        "stitch highlighted pages",
        "unir páginas resaltadas",
        "unir paginas resaltadas",
        "stitch together highlighted pages"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PRINT_HIGHLIGHTED_PAGES__");
    }
}
