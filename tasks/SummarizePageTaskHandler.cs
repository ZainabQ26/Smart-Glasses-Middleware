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
        "resumir página",
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
        return Task.FromResult("__ACTION:SUMMARIZE_CURRENT_PAGE__");
    }
}