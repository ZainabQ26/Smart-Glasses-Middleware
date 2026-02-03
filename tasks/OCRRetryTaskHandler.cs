using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class OCRRetryTaskHandler : ITaskHandler
{
    private readonly ILogger<OCRRetryTaskHandler> _logger;
    private readonly IDocumentProcessingService _processingService;
    private readonly IRAGService _ragService;

    public string Name => "ocr-retry";

    public OCRRetryTaskHandler(ILogger<OCRRetryTaskHandler> logger, IDocumentProcessingService processingService, IRAGService ragService)
    {
        _logger = logger;
        _processingService = processingService;
        _ragService = ragService;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            _logger.LogInformation("OCR retry task requested with input: {Input}", input);

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

            return "OCR retry task handler expects a document ID (GUID).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OCR retry task handler");
            return $"OCR retry error: {ex.Message}";
        }
    }
}
