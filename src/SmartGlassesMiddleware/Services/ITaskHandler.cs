namespace SmartGlassesMiddleware.Services;

public interface ITaskHandler
{
    /// <summary>
    /// Short name of the task this handler implements (lowercase, no spaces).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Trigger keywords/phrases that map directly to this task without AI classification.
    /// Any input containing one of these keywords (case-insensitive) will bypass AI and
    /// go straight to this handler. Leave empty to always rely on AI classification.
    /// </summary>
    IReadOnlyList<string> Keywords { get; }

    /// <summary>
    /// Handle the user's input and return a response string.
    /// </summary>
    Task<string> HandleAsync(string input);
}
