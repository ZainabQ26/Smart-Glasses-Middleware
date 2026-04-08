using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice commands to jump to the previous highlighted match in the current PDF.
/// </summary>
public class PreviousHighlightTaskHandler : ITaskHandler
{
    public string Name => "previous-highlight";

    public IReadOnlyList<string> Keywords =>
    new[]
    {
        "previous highlight",
        "resaltado anterior",
        "prev highlight",
        "back highlight",
        "previous highlighted",
        "previous highlighted word",
        "palabra resaltada anterior",
        "go to previous highlight",
        "ir al resaltado anterior",
        "find previous highlight",
        "encontrar resaltado anterior"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PREVIOUS_HIGHLIGHT__");
    }
}
