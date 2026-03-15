using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "highlight word" voice command.
/// Extracts the word or phrase to highlight from the user's input and returns
/// an action token that the frontend uses to highlight it in the open PDF.
///
/// Expected input examples:
///   "highlight the word example"
///   "highlight security"
///   "highlight patient information"
/// </summary>
public class HighlightWordTaskHandler : ITaskHandler
{
    public string Name => "highlight-word";

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
    /// Strips common leading phrases ("highlight the word", "highlight") from
    /// the raw input to extract the target word or phrase.
    /// </summary>
    private static string ExtractHighlightTarget(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim();

        // Remove leading prefixes like "highlight the word", "highlight word",
        // "highlight the", "highlight" so what remains is the target.
        string[] prefixes =
        [
            "highlight the word ",
            "highlight word ",
            "highlight the phrase ",
            "highlight phrase ",
            "highlight the ",
            "highlight ",
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
