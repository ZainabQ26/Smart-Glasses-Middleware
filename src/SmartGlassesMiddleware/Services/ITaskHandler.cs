namespace SmartGlassesMiddleware.Services;

public interface ITaskHandler
{
    /// <summary>
    /// Short name of the task this handler implements (lowercase, no spaces).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Handle the user's input and return a response string.
    /// </summary>
    Task<string> HandleAsync(string input);
}
