using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice commands to jump to the next highlighted match in the current PDF.
/// </summary>
public class NextHighlightTaskHandler : ITaskHandler
{
    public string Name => "next-highlight";

    public IReadOnlyList<string> Keywords =>
    new[]
    {
        "next highlight",
        "siguiente resaltado",
        "next highlighted",
        "next highlighted word",
        "siguiente palabra resaltada",
        "next highlighted phrase",
        "siguiente frase resaltada",
        "go to next highlight",
        "ir al siguiente resaltado",
        "go to next highlighted word",
        "ir a la siguiente palabra resaltada",
        "find next highlight",
        "encontrar siguiente resaltado"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:NEXT_HIGHLIGHT__");
    }
}
