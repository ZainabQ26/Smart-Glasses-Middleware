using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "highlight word" and "search" voice commands.
/// Extracts the word or phrase to highlight/search from the user's input and
/// returns an action token that the frontend uses to highlight it in the open PDF.
///
/// "Search" and "highlight" are treated as aliases — both highlight the term in
/// the currently open document using the client-side PDF rendering engine.
///
/// Expected input examples:
///   "highlight the word example"
///   "highlight security"
///   "highlight patient information"
///   "search for patient"
///   "find the word security"
///   "search patient information"
/// </summary>
public class HighlightWordTaskHandler : ITaskHandler
{
    public string Name => "highlight-word";

    public IReadOnlyList<string> Keywords => new[]
    {
        "highlight the word", "highlight word", "highlight the phrase", "highlight phrase",
        "highlight the", "highlight", "search for the word", "search for the phrase",
        "search for the", "search for", "find the word", "find the phrase", "find the"
    };

    public Task<string> HandleAsync(string input)
    {
        var word = ExtractHighlightTarget(input);

        if (string.IsNullOrWhiteSpace(word))
        {
            return Task.FromResult("Please say what you want to highlight. For example: \"highlight example\".");
        }

        // Return an action token the frontend intercepts and handles directly.
        return Task.FromResult($"__ACTION:HIGHLIGHT_WORD:{word}__");
    }

    /// <summary>
    /// Strips common leading phrases from the raw input to extract the target
    /// word or phrase. Supports both "highlight" and "search"/"find" prefixes
    /// so either command highlights the same term in the open PDF.
    /// </summary>
    private static string ExtractHighlightTarget(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim();

        // Remove leading prefixes. Order matters — longer/more-specific prefixes first.
        string[] prefixes =
        [
            "highlight the word ",
            "highlight word ",
            "highlight the phrase ",
            "highlight phrase ",
            "highlight the ",
            "highlight ",
            "search for the word ",
            "search for the phrase ",
            "search for the ",
            "search for ",
            "search the word ",
            "search the phrase ",
            "search the ",
            "search ",
            "find the word ",
            "find the phrase ",
            "find the ",
            "find ",
        ];

        foreach (var prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        // Fallback: use the whole input as the target.
        return trimmed;
    }
}
