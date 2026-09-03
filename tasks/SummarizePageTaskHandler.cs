using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice/typed commands that ask for a summary of a specific page or currently visible PDF page.
/// </summary>
public class SummarizePageTaskHandler : ITaskHandler
{
    public string Name => "summarize-page";

    public IReadOnlyList<string> Keywords =>
    [
        "summarize page",
        "resumir página",
        "summarize page 1",
        "summarize page 2",
        "summarize page 3",
        "summarize page 4",
        "summarize page 5",
        "summarize page one",
        "summarize page two",
        "summarize page three",
        "summarize page four",
        "summarize page five",
        "summarize this page",
        "resumir esta página",
        "summarize current page",
        "resumir página actual",
        "summary of this page",
        "resumen de esta página",
        "summary of the page",
        "resumen de la página",
        "what is on this page",
        "qué hay en esta página"
    ];

    public Task<string> HandleAsync(string input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"\b(?:page|página|pagina)\s*(\d{1,4})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var targetPage))
            {
                return Task.FromResult($"__ACTION:SUMMARIZE_PAGE:{targetPage}__");
            }

            var wordMatch = System.Text.RegularExpressions.Regex.Match(input, @"\b(?:page|página|pagina)\s*(one|two|three|four|five|six|seven|eight|nine|ten)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (wordMatch.Success)
            {
                var word = wordMatch.Groups[1].Value.ToLower();
                var p = word switch
                {
                    "one" => 1,
                    "two" => 2,
                    "three" => 3,
                    "four" => 4,
                    "five" => 5,
                    "six" => 6,
                    "seven" => 7,
                    "eight" => 8,
                    "nine" => 9,
                    "ten" => 10,
                    _ => 1
                };
                return Task.FromResult($"__ACTION:SUMMARIZE_PAGE:{p}__");
            }
        }

        return Task.FromResult("__ACTION:SUMMARIZE_CURRENT_PAGE__");
    }
}