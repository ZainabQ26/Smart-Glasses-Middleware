using SmartGlassesMiddleware.Services;

/// <summary>
/// Handles the "ocr-retry" voice command.
/// Retries photo capture and display when voice prompted.
/// </summary>
public class OcrRetryTaskHandler : ITaskHandler
{
    public string Name => "ocr-retry";

    public IReadOnlyList<string> Keywords => new[]
    {
        "retry ocr",
        "redo ocr",
        "redo the ocr",
        "ocr retry",
        "try ocr again",
        "retake picture",
        "retake photo",
        "take picture again",
        "take photo again",
        // Spanish
        "reintentar ocr",
        "repetir ocr",
        "volver a tomar foto",
        "tomar foto de nuevo"
    };

    public Task<string> HandleAsync(string input)
    {
        return Task.FromResult("__ACTION:TAKE_PICTURE__");
    }
}
