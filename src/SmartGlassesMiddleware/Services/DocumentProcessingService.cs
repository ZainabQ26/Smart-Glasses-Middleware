using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using SixLabors.ImageSharp;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Services;

public interface IDocumentProcessingService
{
    Task<DocumentMetadata> ProcessDocumentAsync(Stream fileStream, string fileName, string contentType);
    Task<List<DocumentChunk>> ExtractChunksAsync(Guid documentId);
}

public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly ILogger<DocumentProcessingService> _logger;
    private readonly IMediaStorageService _storageService;
    private readonly ApplicationDbContext _context;
    private const int ChunkSize = 500; // Characters per chunk
    private const int ChunkOverlap = 50; // Overlap between chunks for context

    public DocumentProcessingService(
        ILogger<DocumentProcessingService> logger,
        IMediaStorageService storageService,
        ApplicationDbContext context)
    {
        _logger = logger;
        _storageService = storageService;
        _context = context;
    }

    public async Task<DocumentMetadata> ProcessDocumentAsync(Stream fileStream, string fileName, string contentType)
    {
        var documentType = DetermineDocumentType(fileName, contentType);
        var storageKey = $"documents/{Guid.NewGuid()}/{fileName}";
        
        // Store the original file
        fileStream.Position = 0;
        await _storageService.UploadFileAsync(storageKey, fileStream, contentType);
        
        var metadata = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            OriginalFileName = fileName,
            ContentType = contentType,
            StorageKey = storageKey,
            UploadedAt = DateTime.UtcNow,
            FileSize = fileStream.Length,
            DocumentType = documentType,
            IsProcessed = false
        };

        try
        {
            // Extract text and create chunks
            fileStream.Position = 0;
            var chunks = documentType switch
            {
                "pdf" => await ExtractFromPdfAsync(fileStream, metadata.Id),
                "docx" => await ExtractFromDocxAsync(fileStream, metadata.Id),
                "txt" => await ExtractFromTextAsync(fileStream, metadata.Id),
                "image" => await ExtractFromImageAsync(fileStream, metadata.Id),
                _ => throw new NotSupportedException($"Document type {documentType} is not supported")
            };

            metadata.TotalPages = chunks.Any() ? chunks.Max(c => c.PageNumber) : 0;
            metadata.IsProcessed = true;
            metadata.ProcessedAt = DateTime.UtcNow;
            metadata.Chunks = chunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process document {FileName}", fileName);
            metadata.IsProcessed = false;
            metadata.ProcessingError = ex.Message;
        }

        _context.DocumentMetadatas.Add(metadata);
        await _context.SaveChangesAsync();

        return metadata;
    }

    public async Task<List<DocumentChunk>> ExtractChunksAsync(Guid documentId)
    {
        return await _context.DocumentChunks
            .Where(c => c.DocumentMetadataId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();
    }

    private string DetermineDocumentType(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        return extension switch
        {
            ".pdf" => "pdf",
            ".docx" => "docx",
            ".txt" => "txt",
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tiff" => "image",
            _ => contentType.StartsWith("image/") ? "image" : "unknown"
        };
    }

    private async Task<List<DocumentChunk>> ExtractFromPdfAsync(Stream pdfStream, Guid documentId)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        try
        {
            using var pdfReader = new PdfReader(pdfStream);
            using var pdfDocument = new PdfDocument(pdfReader);

            for (int pageNum = 1; pageNum <= pdfDocument.GetNumberOfPages(); pageNum++)
            {
                var page = pdfDocument.GetPage(pageNum);
                var strategy = new LocationTextExtractionStrategy();
                string pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                // If no text found, try OCR
                if (string.IsNullOrWhiteSpace(pageText))
                {
                    _logger.LogInformation("No text found on page {PageNum}, attempting OCR", pageNum);
                    pageText = await PerformOcrOnPageAsync(page);
                }

                // Create chunks from page text
                var pageChunks = CreateTextChunks(pageText, pageNum, documentId, ref chunkIndex);
                chunks.AddRange(pageChunks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from PDF");
            throw;
        }

        return chunks;
    }

    private async Task<string> PerformOcrOnPageAsync(PdfPage page)
    {
        // Note: This is a simplified OCR implementation
        // In production, you would configure Tesseract properly
        try
        {
            // For now, return empty string as full OCR setup requires Tesseract data files
            // In production, configure: var ocrReader = new Tesseract4OcrEngineProperties().SetPathToTessData("path/to/tessdata");
            _logger.LogWarning("OCR is not fully configured. Tesseract data files required.");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed");
            return string.Empty;
        }
    }

    private async Task<List<DocumentChunk>> ExtractFromDocxAsync(Stream docxStream, Guid documentId)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;
        var pageNum = 1; // DOCX doesn't have explicit pages, we'll treat sections as pages

        try
        {
            using var doc = WordprocessingDocument.Open(docxStream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            
            if (body != null)
            {
                var paragraphs = body.Elements<Paragraph>();
                var fullText = new StringBuilder();

                foreach (var paragraph in paragraphs)
                {
                    fullText.AppendLine(paragraph.InnerText);
                }

                var pageChunks = CreateTextChunks(fullText.ToString(), pageNum, documentId, ref chunkIndex);
                chunks.AddRange(pageChunks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from DOCX");
            throw;
        }

        return chunks;
    }

    private async Task<List<DocumentChunk>> ExtractFromTextAsync(Stream textStream, Guid documentId)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        try
        {
            using var reader = new StreamReader(textStream);
            var text = await reader.ReadToEndAsync();
            var pageChunks = CreateTextChunks(text, 1, documentId, ref chunkIndex);
            chunks.AddRange(pageChunks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from TXT file");
            throw;
        }

        return chunks;
    }

    private async Task<List<DocumentChunk>> ExtractFromImageAsync(Stream imageStream, Guid documentId)
    {
        var chunks = new List<DocumentChunk>();
        
        try
        {
            // For images, we would use OCR
            // This is a placeholder - full implementation would require Tesseract configuration
            _logger.LogWarning("Image OCR not fully implemented. Requires Tesseract configuration.");
            
            // Create a placeholder chunk
            var chunk = new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                ChunkText = "[Image content - OCR not configured]",
                PageNumber = 1,
                ChunkIndex = 0,
                CreatedAt = DateTime.UtcNow
            };
            
            chunks.Add(chunk);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image");
            throw;
        }

        return chunks;
    }

    private List<DocumentChunk> CreateTextChunks(string text, int pageNumber, Guid documentId, ref int chunkIndex)
    {
        var chunks = new List<DocumentChunk>();
        
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        var startIndex = 0;
        
        while (startIndex < text.Length)
        {
            var endIndex = Math.Min(startIndex + ChunkSize, text.Length);
            
            // Try to break at sentence or word boundary
            if (endIndex < text.Length)
            {
                var lastPeriod = text.LastIndexOf('.', endIndex, Math.Min(100, endIndex - startIndex));
                var lastSpace = text.LastIndexOf(' ', endIndex, Math.Min(100, endIndex - startIndex));
                
                if (lastPeriod > startIndex)
                    endIndex = lastPeriod + 1;
                else if (lastSpace > startIndex)
                    endIndex = lastSpace;
            }

            var chunkText = text.Substring(startIndex, endIndex - startIndex).Trim();
            
            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                var chunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentMetadataId = documentId,
                    ChunkText = chunkText,
                    PageNumber = pageNumber,
                    ChunkIndex = chunkIndex++,
                    CreatedAt = DateTime.UtcNow
                };
                
                chunks.Add(chunk);
            }

            startIndex = endIndex - ChunkOverlap;
            if (startIndex < 0) startIndex = 0;
        }

        return chunks;
    }
}
