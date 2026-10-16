using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "close document" voice command.
/// Returns an action token that tells the frontend to hide the inline PDF
/// viewer, returning the user to the main home screen.
/// </summary>
public class CloseDocumentTaskHandler : ITaskHandler
{
    public string Name => "close-document";

    public IReadOnlyList<string> Keywords => new[]
    {
        "close",
        "cerrar",
        "close document",
        "closed document",
        "closing document",
        "cerrar documento",
        "close viewer",
        "close the viewer",
        "cerrar visor",
        "cerrar el visor",
        "close photo",
        "close picture",
        "close image",
        "cerrar foto",
        "cerrar imagen",
        "close pdf",
        "closed pdf",
        "closing pdf",
        "cerrar pdf",
        "exit document",
        "salir del documento",
        "close the document",
        "closed the document",
        "cerrar el documento",
        "close the pdf",
        "closed the pdf",
        "cerrar el pdf",
        "exit",
        "salir"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:CLOSE_DOCUMENT__");
    }
}
