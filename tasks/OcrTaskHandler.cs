using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "ocr" and "take picture" voice command.
/// Returns an action token that tells the frontend to capture a photo
/// with the camera, upload it as a document, and display it in the viewer.
/// </summary>
public class OcrTaskHandler : ITaskHandler
{
    public string Name => "ocr";

    public IReadOnlyList<string> Keywords => new[]
    {
        "scan text",
        "scanned text",
        "scanning text",
        "read text",
        "perform ocr",
        "run ocr",
        "do ocr",
        "ocr",
        "ocr this",
        "scan image",
        "scan document",
        "scan the document",
        "extract text",
        "take picture",
        "take a picture",
        "take photo",
        "take a photo",
        "snap picture",
        "snap photo",
        "snap a picture",
        "snap a photo",
        "capture photo",
        "capture image",
        "capture",
        "photo",
        "picture",
        // Spanish
        "escanear texto",
        "leer texto",
        "extraer texto",
        "realizar ocr",
        "ejecutar ocr",
        "escanear imagen",
        "escanear documento",
        "tomar foto",
        "tomar una foto",
        "sacar foto",
        "sacar una foto",
        "capturar foto",
        "capturar imagen",
        "foto"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:TAKE_PICTURE__");
    }
}
