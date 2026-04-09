using SmartGlassesMiddleware.Services;
using System.Globalization;
using System.Text;

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
    /// Strips diacritics from a string so that prefix matching works regardless
    /// of whether Whisper includes or omits accent marks.
    /// </summary>
    private static string StripDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Strips common leading phrases from the raw input to extract the target
    /// word or phrase. Supports both "highlight" and "search"/"find" prefixes
    /// (English and Spanish) so either command highlights the same term in the
    /// open PDF.
    /// </summary>
    private static string ExtractHighlightTarget(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim();
        // Normalize diacritics for accent-insensitive prefix matching
        var normalizedTrimmed = StripDiacritics(trimmed);

        // Remove leading prefixes. Order matters — longer/more-specific prefixes first.
        // Each entry is (normalizedPrefix, originalPrefixLength).
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
            // Spanish
            "resaltar la palabra ",
            "resaltar la frase ",
            "resaltar el ",
            "resaltar la ",
            "resaltar ",
            "buscar la palabra ",
            "buscar la frase ",
            "buscar el ",
            "buscar la ",
            "buscar ",
            "encontrar la palabra ",
            "encontrar la frase ",
            "encontrar el ",
            "encontrar la ",
            "encontrar ",
        ];

        foreach (var prefix in prefixes)
        {
            var normalizedPrefix = StripDiacritics(prefix);
            if (normalizedTrimmed.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Slice using the normalized prefix length.  All prefixes are ASCII-only
                // so normalizedPrefix.Length == prefix.Length and the lengths of trimmed
                // and normalizedTrimmed are identical (diacritic stripping is a
                // character-by-character replacement that never changes string length in
                // NFC).  We slice trimmed so the original casing/accents are preserved.
                return trimmed[normalizedPrefix.Length..].Trim();
            }
        }

        // Fallback: use the whole input as the target.
        return trimmed;
    }
}
