using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "highlight word" and "search" voice commands.
/// Extracts the word or phrase to highlight/search from the user's input and
/// returns an action token that the frontend uses to highlight it in the open PDF.
///
/// "Search" and "highlight" are treated as aliases — both highlight the term in
/// the currently open document using the client-side PDF rendering engine.
///
/// When FDA abbreviations are available, also highlights any related terms (e.g., if the user
/// highlights "ACE", the handler also highlights "angiotensin converting enzyme" in the same color).
///
/// Expected input examples:
///   "highlight the word example"
///   "highlight security"
///   "highlight patient information"
///   "search for patient"
///   "find the word security"
///   "search patient information"
///   "highlight ACE" (also highlights "angiotensin converting enzyme")
/// </summary>
public class HighlightWordTaskHandler : ITaskHandler
{
    private readonly IAbbreviationService _abbreviationService;

    public string Name => "highlight-word";

    public IReadOnlyList<string> Keywords => new[]
    {
        "highlight the word",
        "resaltar la palabra",
        "highlight word",
        "resaltar palabra",
        "highlight the phrase",
        "resaltar la frase",
        "highlight phrase",
        "resaltar frase",
        "highlight the",
        "resaltar el",
        "highlight",
        "resaltar",
        "highlights the",
        "resalta el",
        "highlights",
        "resaltados",
        "search for the word",
        "buscar la palabra",
        "search for the phrase",
        "buscar la frase",
        "search for the",
        "buscar el",
        "search for",
        "buscar",
        "find the word",
        "encontrar la palabra",
        "find the phrase",
        "encontrar la frase",
        "find the",
        "encontrar el"
    };

    public HighlightWordTaskHandler(IAbbreviationService abbreviationService)
    {
        _abbreviationService = abbreviationService;
    }

    public async Task<string> HandleAsync(string input)
    {
        var word = ExtractHighlightTarget(input);

        if (string.IsNullOrWhiteSpace(word))
        {
            return "Please say what you want to highlight. For example: \"highlight example\".";
        }

        // Look up related abbreviation pairs
        var (relatedTerms, _) = await _abbreviationService.GetRelatedTermsAsync(word);

        // If abbreviations found, include all related terms separated by pipe (|)
        if (relatedTerms.Count > 1)
        {
            var termsStr = string.Join("|", relatedTerms.Distinct().Select(t => t.Trim()));
            return $"__ACTION:HIGHLIGHT_WORD:{termsStr}__";
        }

        // Return an action token the frontend intercepts and handles directly.
        return $"__ACTION:HIGHLIGHT_WORD:{word}__";
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
            "highlights the word ",
            "highlights word ",
            "highlights the phrase ",
            "highlights phrase ",
            "highlight the ",
            "highlight ",
            "highlights the ",
            "highlights ",
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
