using SmartGlassesMiddleware.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;

/// <summary>
/// Handles voice commands that ask to navigate to a specific page number.
/// Converts word numbers ("five", "fifth") to digits ("5") automatically.
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
        "abrir página",
        "page"
    };

    private static readonly Regex PageNumberRegex =
        new Regex(@"\b(\d+)(?:st|nd|rd|th)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> WordToDigitMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "zero", "0" }, { "one", "1" }, { "two", "2" }, { "three", "3" }, { "four", "4" },
        { "five", "5" }, { "six", "6" }, { "seven", "7" }, { "eight", "8" }, { "nine", "9" },
        { "ten", "10" }, { "eleven", "11" }, { "twelve", "12" }, { "thirteen", "13" }, { "fourteen", "14" },
        { "fifteen", "15" }, { "sixteen", "16" }, { "seventeen", "17" }, { "eighteen", "18" }, { "nineteen", "19" },
        { "twenty", "20" }, { "thirty", "30" }, { "forty", "40" }, { "fifty", "50" },
        { "first", "1" }, { "second", "2" }, { "third", "3" }, { "fourth", "4" }, { "fifth", "5" },
        { "sixth", "6" }, { "seventh", "7" }, { "eighth", "8" }, { "ninth", "9" }, { "tenth", "10" }
    };

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

    public static string? ExtractPageNumber(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = NormalizeWordsToDigits(input);
        var match = PageNumberRegex.Match(normalized);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string NormalizeWordsToDigits(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var words = input.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            var clean = Regex.Replace(words[i], @"[^\w]", "");
            if (WordToDigitMap.TryGetValue(clean, out var digit))
            {
                words[i] = Regex.Replace(words[i], @"\b" + Regex.Escape(clean) + @"\b", digit, RegexOptions.IgnoreCase);
            }
        }
        return string.Join(" ", words);
    }
}
