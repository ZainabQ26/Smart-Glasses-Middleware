using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "open admin panel" voice command.
/// Returns an action token that tells the frontend to navigate to the admin panel.
///
/// Expected input examples:
///   "open admin panel"
///   "go to admin"
///   "show admin"
///   "admin panel"
/// </summary>
public class AdminPanelTaskHandler : ITaskHandler
{
    public string Name => "open-admin-panel";

    public IReadOnlyList<string> Keywords => new[]
    {
        "open admin panel",
        "abrir panel de control",
        "go to admin",
        "ir a administración",
        "show admin",
        "mostrar administración",
        "admin panel",
        "panel de control",
        "admin",
        "administración"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:OPEN_ADMIN_PANEL__");
    }
}
