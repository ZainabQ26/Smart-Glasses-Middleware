using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice commands to highlight text in the currently open document.
/// Extracts the word or phrase from the user's input and returns an action
/// token that the frontend uses to highlight it in the open PDF viewer,
/// without ever requiring a document ID.
/// </summary>
public class DocumentHighlightTaskHandler : ITaskHandler
{
    public string Name => "document-highlight";

    public IReadOnlyList<string> Keywords => new[]
    {
        "highlight in document", "document highlight"
    };

    public Task<string> HandleAsync(string input)
    {
        var highlightText = ExtractHighlightTarget(input);

        if (string.IsNullOrWhiteSpace(highlightText))
        {
            return Task.FromResult("Please say what you want to highlight. For example: \"highlight example\".");
        }

        // Return an action token — the frontend highlights the text in the
        // currently open PDF without needing a document ID.
        return Task.FromResult($"__ACTION:HIGHLIGHT_WORD:{highlightText}__");
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
            "document highlight ",
            "highlight the ",
            "highlight ",
        ];

        foreach (var prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Trim();
        }

        return trimmed;
    }
}
