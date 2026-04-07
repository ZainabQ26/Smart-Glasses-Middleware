using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice commands to jump to the next highlighted match in the current PDF.
/// </summary>
public class NextHighlightTaskHandler : ITaskHandler
{
    public string Name => "next-highlight";

    public IReadOnlyList<string> Keywords => new[]
    {
        "next highlight",
        "next highlighted",
        "next highlighted word",
        "next highlighted phrase",
        "go to next highlight",
        "go to next highlighted word",
        "find next highlight"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:NEXT_HIGHLIGHT__");
    }
}
