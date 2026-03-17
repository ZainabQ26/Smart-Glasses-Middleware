using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "remove highlights" / "clear highlights" voice command.
/// Returns an action token that tells the frontend to wipe all keyword
/// highlights from the currently open PDF.
/// </summary>
public class RemoveHighlightsTaskHandler : ITaskHandler
{
    public string Name => "remove-highlights";

    public IReadOnlyList<string> Keywords => new[]
    {
        "remove highlights", "clear highlights", "delete highlights", "remove all highlights",
        "clear all highlights", "remove highlight", "clear highlight"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:REMOVE_HIGHLIGHTS__");
    }
}
