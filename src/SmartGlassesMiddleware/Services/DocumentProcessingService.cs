using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Geom;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using iText.IO.Image;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Services;

public interface IDocumentProcessingService
{
    Task<DocumentMetadata> ProcessDocumentAsync(Stream fileStream, string fileName, string contentType, string? requestId = null);
    Task<List<DocumentChunk>> ExtractChunksAsync(Guid documentId);
    Task<DocumentMetadata> ReprocessDocumentAsync(Guid documentId);
}

public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly ILogger<DocumentProcessingService> _logger;
    private readonly IMediaStorageService _storageService;
    private readonly ApplicationDbContext _context;
    private readonly IRequestMonitoringService? _monitoringService;
    private readonly IConfiguration _configuration;
    private const int ChunkSize = 500; // Characters per chunk
    private const int ChunkOverlap = 50; // Overlap between chunks for context

    public DocumentProcessingService(
        ILogger<DocumentProcessingService> logger,
        IMediaStorageService storageService,
        ApplicationDbContext context,
        IRequestMonitoringService? monitoringService,
        IConfiguration configuration)
    {
        _logger = logger;
        _storageService = storageService;
        _context = context;
        _monitoringService = monitoringService;
        _configuration = configuration;
    }

    public async Task<DocumentMetadata> ProcessDocumentAsync(Stream fileStream, string fileName, string contentType, string? requestId = null)
    {
        var documentType = DetermineDocumentType(fileName, contentType);
        var documentId = Guid.NewGuid();
        
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", $"Converting {fileName} to PDF", 10);
        }

        // Convert to PDF and get the converted stream
        MemoryStream pdfStream;
        string pdfFileName;
        
        try
        {
            fileStream.Position = 0;
            (pdfStream, pdfFileName) = await ConvertToPdfAsync(fileStream, fileName, documentType, requestId);
            
            if (requestId != null && _monitoringService != null)
            {
                await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Uploading converted PDF", 40);
            }

            // Store only the converted PDF
            var storageKey = $"documents/{documentId}/{pdfFileName}";
            pdfStream.Position = 0;
            await _storageService.UploadFileAsync(storageKey, pdfStream, "application/pdf");

            var metadata = new DocumentMetadata
            {
                Id = documentId,
                FileName = pdfFileName,
                OriginalFileName = fileName,
                ContentType = "application/pdf",
                StorageKey = storageKey,
                UploadedAt = DateTime.UtcNow,
                FileSize = pdfStream.Length,
                DocumentType = "pdf",
                IsProcessed = false
            };

            if (requestId != null && _monitoringService != null)
            {
                await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Extracting text from PDF", 60);
            }

            // Extract text and create chunks from the PDF
            pdfStream.Position = 0;
            var chunks = await ExtractFromPdfAsync(pdfStream, metadata.Id, requestId);

            metadata.TotalPages = chunks.Any() ? chunks.Max(c => c.PageNumber) : 0;
            metadata.IsProcessed = true;
            metadata.ProcessedAt = DateTime.UtcNow;
            metadata.Chunks = chunks;

            _context.DocumentMetadatas.Add(metadata);
            await _context.SaveChangesAsync();

            if (requestId != null && _monitoringService != null)
            {
                await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Document processed successfully", 90);
            }

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process document {FileName}", fileName);
            
            var metadata = new DocumentMetadata
            {
                Id = documentId,
                FileName = fileName,
                OriginalFileName = fileName,
                ContentType = contentType,
                StorageKey = string.Empty,
                UploadedAt = DateTime.UtcNow,
                FileSize = 0,
                DocumentType = documentType,
                IsProcessed = false,
                ProcessingError = ex.Message
            };

            _context.DocumentMetadatas.Add(metadata);
            await _context.SaveChangesAsync();

            throw;
        }
    }

    private async Task<(MemoryStream pdfStream, string pdfFileName)> ConvertToPdfAsync(Stream inputStream, string fileName, string documentType, string? requestId)
    {
        var baseFileName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var pdfFileName = $"{baseFileName}.pdf";

        switch (documentType)
        {
            case "pdf":
                // Already a PDF, just copy it
                var ms = new MemoryStream();
                inputStream.Position = 0;
                await inputStream.CopyToAsync(ms);
                return (ms, pdfFileName);

            case "docx":
                return await ConvertDocxToPdfAsync(inputStream, pdfFileName, requestId);

            case "txt":
                return await ConvertTextToPdfAsync(inputStream, pdfFileName, requestId);

            case "image":
                return await ConvertImageToPdfAsync(inputStream, pdfFileName, requestId);

            default:
                throw new NotSupportedException($"Document type {documentType} is not supported");
        }
    }

    private async Task<(MemoryStream, string)> ConvertDocxToPdfAsync(Stream docxStream, string pdfFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting DOCX to PDF", 20);
        }

        var pdfStream = new MemoryStream();
        using (var pdfWriter = new PdfWriter(pdfStream, new WriterProperties().SetPdfVersion(PdfVersion.PDF_1_7)))
        {
            pdfWriter.SetCloseStream(false);
            using var pdfDocument = new PdfDocument(pdfWriter);
            using var document = new iText.Layout.Document(pdfDocument);

            // Extract text from DOCX
            using var doc = WordprocessingDocument.Open(docxStream, false);
            var body = doc.MainDocumentPart?.Document?.Body;

            if (body != null)
            {
                foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                {
                    var text = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        document.Add(new iText.Layout.Element.Paragraph(text));
                    }
                }
            }
        }

        pdfStream.Position = 0;
        return (pdfStream, pdfFileName);
    }

    private async Task<(MemoryStream, string)> ConvertTextToPdfAsync(Stream textStream, string pdfFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting text to PDF", 20);
        }

        var pdfStream = new MemoryStream();
        using (var pdfWriter = new PdfWriter(pdfStream, new WriterProperties().SetPdfVersion(PdfVersion.PDF_1_7)))
        {
            pdfWriter.SetCloseStream(false);
            using var pdfDocument = new PdfDocument(pdfWriter);
            using var document = new iText.Layout.Document(pdfDocument);

            using var reader = new StreamReader(textStream);
            var text = await reader.ReadToEndAsync();
            
            // Split text into paragraphs and add to PDF
            var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None);
            foreach (var para in paragraphs)
            {
                if (!string.IsNullOrWhiteSpace(para))
                {
                    document.Add(new iText.Layout.Element.Paragraph(para.Trim()));
                }
            }
        }

        pdfStream.Position = 0;
        return (pdfStream, pdfFileName);
    }

    private async Task<(MemoryStream, string)> ConvertImageToPdfAsync(Stream imageStream, string pdfFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Detecting and cropping paper", 15);
        }

        // Load image
        imageStream.Position = 0;
        using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgb24>(imageStream);
        
        // Crop to paper if possible
        var croppedImage = CropToPaper(image);
        
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Performing OCR on image", 25);
        }

        // Perform OCR on the cropped image
        var ocrText = await PerformOcrOnImageAsync(croppedImage);

        // Create PDF with image and OCR text
        var pdfStream = new MemoryStream();
        using (var pdfWriter = new PdfWriter(pdfStream, new WriterProperties().SetPdfVersion(PdfVersion.PDF_1_7)))
        {
            pdfWriter.SetCloseStream(false);
            using var pdfDocument = new PdfDocument(pdfWriter);
            using var document = new iText.Layout.Document(pdfDocument);

            // Add the image to PDF
            using var imageMemStream = new MemoryStream();
            await croppedImage.SaveAsPngAsync(imageMemStream);
            imageMemStream.Position = 0;
            
            var imageData = ImageDataFactory.Create(imageMemStream.ToArray());
            var pdfImage = new iText.Layout.Element.Image(imageData);
            
            // Scale image to fit page
            var pageSize = pdfDocument.GetDefaultPageSize();
            pdfImage.ScaleToFit(pageSize.GetWidth() - 72, pageSize.GetHeight() - 72);
            pdfImage.SetHorizontalAlignment(HorizontalAlignment.CENTER);
            
            document.Add(pdfImage);

            // Add OCR text as invisible overlay for searchability
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                document.Add(new iText.Layout.Element.Paragraph(ocrText)
                    .SetFontSize(0.1f) // Very small font
                    .SetOpacity(0)); // Invisible
            }
        }

        pdfStream.Position = 0;
        return (pdfStream, pdfFileName);
    }

    private SixLabors.ImageSharp.Image<Rgb24> CropToPaper(SixLabors.ImageSharp.Image<Rgb24> image)
    {
        try
        {
            // Simple paper detection: find bounding box of non-white pixels
            int minX = image.Width, minY = image.Height, maxX = 0, maxY = 0;
            bool foundContent = false;

            // Sample the image to detect paper bounds
            for (int y = 0; y < image.Height; y += 5) // Sample every 5 pixels for performance
            {
                for (int x = 0; x < image.Width; x += 5)
                {
                    var pixel = image[x, y];
                    // Check if pixel is not white (threshold 240)
                    if (pixel.R < 240 || pixel.G < 240 || pixel.B < 240)
                    {
                        foundContent = true;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (foundContent && maxX > minX && maxY > minY)
            {
                // Add small padding
                int padding = 20;
                minX = Math.Max(0, minX - padding);
                minY = Math.Max(0, minY - padding);
                maxX = Math.Min(image.Width - 1, maxX + padding);
                maxY = Math.Min(image.Height - 1, maxY + padding);

                var width = maxX - minX;
                var height = maxY - minY;

                if (width > 0 && height > 0)
                {
                    var cropped = image.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(minX, minY, width, height)));
                    _logger.LogInformation("Cropped image from {OrigWidth}x{OrigHeight} to {NewWidth}x{NewHeight}",
                        image.Width, image.Height, width, height);
                    return cropped;
                }
            }

            // If no content found or cropping failed, return original
            return image.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to crop image, using original");
            return image.Clone();
        }
    }

    private async Task<string> PerformOcrOnImageAsync(SixLabors.ImageSharp.Image<Rgb24> image)
    {
        try
        {
            var tessDataPath = _configuration["OCR:TessDataPath"] ?? "/usr/share/tesseract-ocr/5/tessdata";
            
            // Check if Tesseract data exists
            if (!Directory.Exists(tessDataPath))
            {
                _logger.LogWarning("Tesseract data not found at {Path}. OCR disabled.", tessDataPath);
                return string.Empty;
            }

            using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
            
            // Convert ImageSharp image to Pix format for Tesseract
            using var memStream = new MemoryStream();
            await image.SaveAsPngAsync(memStream);
            memStream.Position = 0;
            
            using var pix = Pix.LoadFromMemory(memStream.ToArray());
            using var page = engine.Process(pix);
            
            var text = page.GetText();
            _logger.LogInformation("OCR extracted {Length} characters", text?.Length ?? 0);
            
            return text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR failed, continuing without text extraction");
            return string.Empty;
        }
    }

    public async Task<List<DocumentChunk>> ExtractChunksAsync(Guid documentId)
    {
        return await _context.DocumentChunks
            .Where(c => c.DocumentMetadataId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();
    }

    public async Task<DocumentMetadata> ReprocessDocumentAsync(Guid documentId)
    {
        var metadata = await _context.DocumentMetadatas
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (metadata == null)
            throw new InvalidOperationException($"Document {documentId} not found");

        metadata.IsProcessed = false;
        metadata.ProcessingError = null;
        metadata.ProcessedAt = null;

        if (metadata.Chunks.Any())
        {
            _context.DocumentChunks.RemoveRange(metadata.Chunks);
            metadata.Chunks.Clear();
            await _context.SaveChangesAsync();
        }

        using var stream = await _storageService.DownloadFileAsync(metadata.StorageKey);
        stream.Position = 0;

        // Re-extract from the stored PDF
        var chunks = await ExtractFromPdfAsync(stream, metadata.Id, null);

        metadata.TotalPages = chunks.Any() ? chunks.Max(c => c.PageNumber) : 0;
        metadata.IsProcessed = true;
        metadata.ProcessedAt = DateTime.UtcNow;
        metadata.Chunks = chunks;

        await _context.SaveChangesAsync();
        return metadata;
    }

    private string DetermineDocumentType(string fileName, string contentType)
    {
        var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".pdf" => "pdf",
            ".docx" => "docx",
            ".txt" => "txt",
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tiff" or ".gif" => "image",
            _ => contentType.StartsWith("image/") ? "image" : "unknown"
        };
    }

    private async Task<List<DocumentChunk>> ExtractFromPdfAsync(Stream pdfStream, Guid documentId, string? requestId)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        try
        {
            using var pdfReader = new PdfReader(pdfStream);
            using var pdfDocument = new PdfDocument(pdfReader);
            var totalPages = pdfDocument.GetNumberOfPages();

            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                if (requestId != null && _monitoringService != null && pageNum % 5 == 0)
                {
                    var progress = 60 + (int)((pageNum / (double)totalPages) * 25);
                    await _monitoringService.UpdateRequestStatus(requestId, "Processing", 
                        $"Extracting text: page {pageNum}/{totalPages}", progress);
                }

                var page = pdfDocument.GetPage(pageNum);
                var strategy = new LocationTextExtractionStrategy();
                string pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

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

            // Ensure we always advance to prevent infinite loops
            var nextStart = endIndex - ChunkOverlap;
            startIndex = Math.Max(nextStart, startIndex + 1);
        }

        return chunks;
    }
}
