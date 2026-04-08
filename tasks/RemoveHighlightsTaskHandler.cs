using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "remove highlights" / "clear highlights" voice command.
/// Returns an action token that tells the frontend to wipe all keyword
/// highlights from the currently open PDF.
/// </summary>
public class RemoveHighlightsTaskHandler : ITaskHandler
{
    public string Name => "remove-highlights";

    public IReadOnlyList<string> Keywords =>
    new[]
    {
        "remove highlights",
        "eliminar resaltados",
        "clear highlights",
        "borrar resaltados",
        "delete highlights",
        "remove all highlights",
        "eliminar todos los resaltados",
        "clear all highlights",
        "borrar todos los resaltados",
        "remove highlight",
        "eliminar resaltado",
        "clear highlight",
        "borrar resaltado"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:REMOVE_HIGHLIGHTS__");
    }
}
