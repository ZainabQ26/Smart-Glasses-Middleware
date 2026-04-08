using SmartGlassesMiddleware.Services;
using System.Text.RegularExpressions;

/// <summary>
/// Handles voice commands that ask to navigate to a specific page number.
///
/// Expected input examples:
///   "go to page 5"
///   "jump to page 12"
///   "page 3"
///   "navigate to page 7"
///   "turn to page 2"
/// </summary>
public class GoToPageTaskHandler : ITaskHandler
{
    public string Name => "go-to-page";

    public IReadOnlyList<string> Keywords => new[]
    {
        "go to page",
        "ir a página",
        "jump to page",
        "saltar a página",
        "navigate to page",
        "navegar a página",
        "turn to page",
        "cambiar a página",
        "open page",
        "abrir página"
    };

    // Matches one or more digits optionally preceded by ordinal suffixes (e.g. "1st", "2nd").
    private static readonly Regex PageNumberRegex =
        new Regex(@"\b(\d+)(?:st|nd|rd|th)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<string> HandleAsync(string input)
    {
        var pageNumber = ExtractPageNumber(input);

        if (pageNumber is null)
        {
            return Task.FromResult(
                "Please say a page number. For example: \"go to page 5\".");
        }

        return Task.FromResult($"__ACTION:GO_TO_PAGE:{pageNumber}__");
    }

    /// <summary>
    /// Extracts the first integer from <paramref name="input"/> and returns it
    /// as a string.  Returns <c>null</c> if no number is found.
    /// </summary>
    private static string? ExtractPageNumber(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var match = PageNumberRegex.Match(input);
        return match.Success ? match.Groups[1].Value : null;
    }
}
