using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice commands to highlight text in the currently open document.
/// Extracts the word or phrase from the user's input and returns an action
/// token that the frontend uses to highlight it in the open PDF viewer,
/// without ever requiring a document ID.
///
/// When FDA abbreviations are available, also highlights related terms in the same color
/// (e.g., if the user asks "highlight ACE", both "ACE" and "angiotensin converting enzyme"
/// are highlighted).
/// </summary>
public class DocumentHighlightTaskHandler : ITaskHandler
{
    private readonly IAbbreviationService _abbreviationService;

    public string Name => "document-highlight";

    public IReadOnlyList<string> Keywords => new[]
    {
        "highlight in document", "document highlight", "highlights in document", "document highlights"
    };

    public DocumentHighlightTaskHandler(IAbbreviationService abbreviationService)
    {
        _abbreviationService = abbreviationService;
    }

    public async Task<string> HandleAsync(string input)
    {
        var highlightText = ExtractHighlightTarget(input);

        if (string.IsNullOrWhiteSpace(highlightText))
        {
            return "Please say what you want to highlight. For example: \"highlight example\".";
        }

        // Look up related abbreviation pairs
        var (relatedTerms, _) = await _abbreviationService.GetRelatedTermsAsync(highlightText);

        // If abbreviations found, include all related terms separated by pipe (|)
        if (relatedTerms.Count > 1)
        {
            var termsStr = string.Join("|", relatedTerms.Distinct().Select(t => t.Trim()));
            return $"__ACTION:HIGHLIGHT_WORD:{termsStr}__";
        }

        // Return an action token — the frontend highlights the text in the
        // currently open PDF without needing a document ID.
        return $"__ACTION:HIGHLIGHT_WORD:{highlightText}__";
    }

    private static string ExtractHighlightTarget(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var trimmed = input.Trim();
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
            "document highlight ",
            "document highlights ",
            "highlight the ",
            "highlight ",
            "highlights the ",
            "highlights ",
        ];

        foreach (var prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Trim();
        }

        return trimmed;
    }
}
