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
        "imprimir páginas con resaltados",
        "imprimir paginas con resaltados",
        "imprimir paginas con destacados",
        "print highlighted",
        "imprimir resaltados",
        "imprimir resaltado",
        "imprimir el resaltado",
        "imprimir los resaltados",
        "imprimir las paginas con resaltados",
        "imprimir las paginas con destacados",
        "imprimir destacadas",
        "imprimir destacada",
        "imprimir destacado",
        "imprimir los destacados",
        "imprimir las destacadas",
        "imprimir marcadas",
        "imprimir marcada",
        "imprimir marcado",
        "imprimir los marcados",
        "imprimir las marcadas",
        "imprimir subrayadas",
        "imprimir subrayada",
        "imprimir subrayado",
        "imprimir los subrayados",
        "imprimir las subrayadas",
        "stitch highlighted pages",
        "unir páginas resaltadas",
        "unir paginas resaltadas",
        "stitch together highlighted pages"
    };

    public bool IsMatch(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var lowered = input.ToLowerInvariant();

        if ((lowered.Contains("imprimir") || lowered.Contains("print") || lowered.Contains("unir") || lowered.Contains("stitch")) &&
            (lowered.Contains("resalt") || lowered.Contains("destac") || lowered.Contains("marcad") || lowered.Contains("subray") || lowered.Contains("highlight")))
        {
            return true;
        }

        return Keywords.Any(k => lowered.Contains(k.ToLowerInvariant()));
    }

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PRINT_HIGHLIGHTED_PAGES__");
    }
}
