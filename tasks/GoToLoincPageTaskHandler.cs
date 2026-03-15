using SmartGlassesMiddleware.Services;
using System.Text.RegularExpressions;

/// <summary>
/// Handles voice commands that ask to navigate to a LOINC code within the open
/// PDF document.  The frontend will search the document text for the code and
/// jump to the first page on which it appears, also highlighting it.
///
/// LOINC codes consist of a numeric component number, a hyphen, and a single
/// check-digit (e.g. "8480-6", "718-7", "1558-6").
///
/// Expected input examples:
///   "go to LOINC code 8480-6"
///   "find LOINC 1558-6"
///   "navigate to LOINC code 718-7"
///   "show me LOINC 8480 dash 6"
/// </summary>
public class GoToLoincPageTaskHandler : ITaskHandler
{
    public string Name => "go-to-loinc";

    // Standard LOINC code pattern: one or more digits, a hyphen, one or more digits.
    // While the check-digit in most LOINC codes is a single digit, using \d+
    // is more resilient to any future variations in the standard.
    private static readonly Regex LoincRegex =
        new Regex(@"\b(\d+-\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Spoken variant: "8480 dash 6" → "8480-6"
    private static readonly Regex SpokenLoincRegex =
        new Regex(@"\b(\d+)\s+dash\s+(\d)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<string> HandleAsync(string input)
    {
        var code = ExtractLoincCode(input);

        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(
                "Please say a LOINC code. For example: \"go to LOINC code 8480-6\".");
        }

        return Task.FromResult($"__ACTION:GO_TO_LOINC:{code}__");
    }

    /// <summary>
    /// Extracts a LOINC code from <paramref name="input"/>.
    /// Handles both the written form ("8480-6") and spoken form ("8480 dash 6").
    /// Returns <c>null</c> if no code is found.
    /// </summary>
    private static string? ExtractLoincCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // Try spoken form first ("8480 dash 6").
        var spokenMatch = SpokenLoincRegex.Match(input);
        if (spokenMatch.Success)
            return $"{spokenMatch.Groups[1].Value}-{spokenMatch.Groups[2].Value}";

        // Try written form ("8480-6").
        var writtenMatch = LoincRegex.Match(input);
        if (writtenMatch.Success)
            return writtenMatch.Groups[1].Value;

        return null;
    }
}
