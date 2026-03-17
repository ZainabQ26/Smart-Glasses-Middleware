using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles voice commands that ask for a summary of the currently visible PDF page.
/// The frontend resolves the current page text and sends it to a dedicated Bedrock
/// summary endpoint, so this task does not route through AdvancedTaskHandler.
/// </summary>
public class SummarizePageTaskHandler : ITaskHandler
{
    public string Name => "summarize-page";

    public IReadOnlyList<string> Keywords =>
    [
        "summarize page",
        "summarize this page",
        "summarize current page",
        "summary of this page",
        "summary of the page",
        "what is on this page"
    ];

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:SUMMARIZE_CURRENT_PAGE__");
    }
}