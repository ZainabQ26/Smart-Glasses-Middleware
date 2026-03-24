using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "print highlighted pages" voice command.
/// Returns an action token instructing the frontend to print only pages that
/// contain at least one active highlight.
/// </summary>
public class PrintHighlightedPagesTaskHandler : ITaskHandler
{
    public string Name => "print-highlighted-pages";

    public IReadOnlyList<string> Keywords => new[]
    {
        "print highlighted pages",
        "print highlighted page",
        "print only highlighted pages",
        "print only highlighted page",
        "print highlights only",
        "print just highlighted pages",
        "print pages with highlights",
        "print highlighted",
        "stitch highlighted pages",
        "stitch together highlighted pages"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PRINT_HIGHLIGHTED_PAGES__");
    }
}
