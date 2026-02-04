using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Local task handler for OCR (Optical Character Recognition) of images.
/// Uses Ollama for local processing.
/// Note: This is a placeholder implementation. Full OCR requires Tesseract setup.
/// </summary>
public class OCRTaskHandler : ITaskHandler
{
    private readonly ILogger<OCRTaskHandler> _logger;
    private readonly IDocumentProcessingService _processingService;
    private readonly IRAGService _ragService;

    public string Name => "ocr";

    public OCRTaskHandler(ILogger<OCRTaskHandler> logger, IDocumentProcessingService processingService, IRAGService ragService)
    {
        _logger = logger;
        _processingService = processingService;
        _ragService = ragService;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            // This is a simplified OCR handler
            // In a full implementation, this would:
            // 1. Accept an image file path or document ID
            // 2. Process the image with Tesseract OCR
            // 3. Return extracted text

            _logger.LogInformation("OCR task requested with input: {Input}", input);

            // Check if input is a file path or document ID
            if (Guid.TryParse(input.Trim(), out var documentId))
            {
                var metadata = await _processingService.ReprocessDocumentAsync(documentId);
                await _ragService.GenerateEmbeddingsAsync(documentId);

                return $"Manual OCR retry complete for document {documentId}. " +
                    $"Chunks: {metadata.Chunks.Count}. " +
                    $"ProcessedAt: {metadata.ProcessedAt:O}. " +
                    "If OCR is not configured, pages without text may still be empty. " +
                    "Configure Tesseract and retry to enable full OCR.";
            }

            return "OCR task handler is ready but requires Tesseract configuration.\n\n" +
                   "Usage: Provide a document ID or image file path.\n" +
                   "Example: 'ocr [document-id]'\n\n" +
                   "To enable full OCR:\n" +
                   "- Install Tesseract OCR library\n" +
                   "- Download language data files (tessdata)\n" +
                   "- Update configuration with tessdata path\n" +
                   "- OCR will be automatically applied during document upload";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OCR task handler");
            return $"OCR error: {ex.Message}";
        }
    }
}
