using SmartGlassesMiddleware.Services;

// This is a simple template you can drop into the `tasks/` folder as a
// compiled plugin DLL, or add the same file to the project under
// `src/SmartGlassesMiddleware/Services/` to have it compiled with the app.

public class TemplateTaskHandler : ITaskHandler
{
    // The task name should be lowercase and unique.
    public string Name => "template";

    public IReadOnlyList<string> Keywords => Array.Empty<string>(); // no direct keyword trigger

    // Implement your task logic here. Return a user-facing response string.
    public Task<string> HandleAsync(string input)
    {
        // Example: echo the input with a short confirmation.
        var response = $"Template handler received: {input}";
        return Task.FromResult(response);
    }
}
