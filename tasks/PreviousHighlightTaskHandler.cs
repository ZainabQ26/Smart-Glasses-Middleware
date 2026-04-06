using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice commands to jump to the previous highlighted match in the current PDF.
/// </summary>
public class PreviousHighlightTaskHandler : ITaskHandler
{
    public string Name => "previous-highlight";

    public IReadOnlyList<string> Keywords => new[]
    {
        "previous highlight",
        "prev highlight",
        "back highlight",
        "previous highlighted",
        "previous highlighted word",
        "go to previous highlight",
        "find previous highlight"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:PREVIOUS_HIGHLIGHT__");
    }
}
