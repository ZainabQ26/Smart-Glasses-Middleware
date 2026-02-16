using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Geom;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Markdig;

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
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", $"Converting {fileName} to text-based PDF", 10);
        }

        // Convert to text-based PDF and get the converted stream
        MemoryStream pdfStream;
        string pdfFileName;
        
        try
        {
            fileStream.Position = 0;
            (pdfStream, pdfFileName) = await ConvertToTextPdfAsync(fileStream, fileName, documentType, requestId);
            
            if (requestId != null && _monitoringService != null)
            {
                await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Uploading converted PDF", 40);
            }

            // Store only the converted text-based PDF
            var storageKey = $"documents/{documentId}/{pdfFileName}";
            pdfStream.Position = 0;
            await _storageService.UploadFileAsync(storageKey, pdfStream, "application/pdf");

            // Compute content hash for ManagedFile
            pdfStream.Position = 0;
            var contentHash = _storageService.ComputeContentHash(pdfStream);

            // Create ManagedFile entry so it shows up in Files tab
            var managedFile = new ManagedFile
            {
                Id = Guid.NewGuid(),
                FileName = pdfFileName,
                Label = $"Processed: {fileName}",
                ContentType = "application/pdf",
                StorageKey = storageKey,
                ContentHash = contentHash,
                UploadedAt = DateTime.UtcNow,
                FileSize = pdfStream.Length
            };

            _context.ManagedFiles.Add(managedFile);

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
                IsProcessed = false,
                ManagedFileId = managedFile.Id
            };

            if (requestId != null && _monitoringService != null)
            {
                await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Extracting text with location data from PDF", 60);
            }

            // Extract text and create chunks from the PDF with location data
            pdfStream.Position = 0;
            var chunks = await ExtractFromPdfWithLocationAsync(pdfStream, metadata.Id, requestId);

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

    private async Task<(MemoryStream pdfStream, string pdfFileName)> ConvertToTextPdfAsync(Stream inputStream, string fileName, string documentType, string? requestId)
    {
        var baseFileName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var pdfFileName = $"{baseFileName}.pdf";

        MemoryStream convertedStream;

        switch (documentType)
        {
            case "pdf":
                // PDF is already in PDF format, but we need to ensure it's text-based (searchable)
                convertedStream = await EnsureTextBasedPdfAsync(inputStream, requestId);
                break;

            case "docx":
                convertedStream = await ConvertDocxToPdfAsync(inputStream, pdfFileName, requestId);
                break;

            case "txt":
                convertedStream = await ConvertTextToPdfAsync(inputStream, pdfFileName, requestId);
                break;

            case "xml":
                convertedStream = await ConvertXmlToPdfAsync(inputStream, pdfFileName, requestId);
                break;

            case "image":
                convertedStream = await ConvertImageToPdfAsync(inputStream, pdfFileName, requestId);
                break;

            default:
                throw new NotSupportedException($"Document type {documentType} is not supported");
        }

        // Generate a meaningful filename
        convertedStream.Position = 0;
        var slug = await GenerateSlugFromPdfAsync(convertedStream, baseFileName);
        var finalFileName = $"{slug}.pdf";

        // Reset stream position for downstream consumers
        convertedStream.Position = 0;
        return (convertedStream, finalFileName);
    }

    private async Task<MemoryStream> EnsureTextBasedPdfAsync(Stream pdfStream, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Ensuring PDF is text-based", 20);
        }

        try
        {
            // Read the PDF and check if it has text layers
            pdfStream.Position = 0;
            using var pdfReader = new PdfReader(pdfStream);
            using var pdfDocument = new PdfDocument(pdfReader);
            
            var outputStream = new MemoryStream();
            using var writer = new PdfWriter(outputStream);
            writer.SetCloseStream(false);
            using var outPdf = new PdfDocument(writer);
            
            // Copy all pages to ensure they're properly formatted
            pdfDocument.CopyPagesTo(1, pdfDocument.GetNumberOfPages(), outPdf);
            outPdf.Close();
            
            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring PDF is text-based");
            throw;
        }
    }

    private async Task<MemoryStream> ConvertDocxToPdfAsync(Stream docxStream, string pdfFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting DOCX to PDF", 20);
        }

        var outputStream = new MemoryStream();
        
        try
        {
            using var writer = new PdfWriter(outputStream);
            writer.SetCloseStream(false);
            using var pdf = new PdfDocument(writer);
            using var document = new iText.Layout.Document(pdf);

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
                        // Check if it's a heading based on style
                        var pPr = paragraph.Elements<ParagraphProperties>().FirstOrDefault();
                        var styleId = pPr?.ParagraphStyleId?.Val?.Value;
                        
                        if (styleId != null && styleId.StartsWith("Heading"))
                        {
                            // Add as heading with larger font
                            var para = new iText.Layout.Element.Paragraph(text)
                                .SetFontSize(16)
                                .SetBold();
                            document.Add(para);
                        }
                        else
                        {
                            // Add as regular paragraph
                            document.Add(new iText.Layout.Element.Paragraph(text));
                        }
                    }
                }
            }

            document.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting DOCX to PDF");
            throw;
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private async Task<MemoryStream> ConvertTextToPdfAsync(Stream textStream, string pdfFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting text to PDF", 20);
        }

        var outputStream = new MemoryStream();
        
        try
        {
            using var reader = new StreamReader(textStream);
            var text = await reader.ReadToEndAsync();

            using var writer = new PdfWriter(outputStream);
            writer.SetCloseStream(false);
            using var pdf = new PdfDocument(writer);
            using var document = new iText.Layout.Document(pdf);

            // Add text content
            document.Add(new iText.Layout.Element.Paragraph(text));
            document.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting text to PDF");
            throw;
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private async Task<MemoryStream> ConvertXmlToPdfAsync(Stream xmlStream, string pdfFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting XML to PDF", 20);
        }

        var outputStream = new MemoryStream();
        
        try
        {
            using var reader = new StreamReader(xmlStream);
            var xmlContent = await reader.ReadToEndAsync();

            using var writer = new PdfWriter(outputStream);
            writer.SetCloseStream(false);
            using var pdf = new PdfDocument(writer);
            using var document = new iText.Layout.Document(pdf);

            // Parse XML and add to PDF
            try
            {
                var xdoc = XDocument.Parse(xmlContent);
                document.Add(new iText.Layout.Element.Paragraph("XML Content").SetFontSize(16).SetBold());
                document.Add(new iText.Layout.Element.Paragraph(xdoc.ToString()));
            }
            catch
            {
                // If parsing fails, just add raw XML
                document.Add(new iText.Layout.Element.Paragraph(xmlContent));
            }

            document.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting XML to PDF");
            throw;
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private async Task<MemoryStream> ConvertImageToPdfAsync(Stream imageStream, string pdfFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting image to PDF", 20);
        }

        var outputStream = new MemoryStream();
        
        try
        {
            // Load and crop image
            imageStream.Position = 0;
            using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgb24>(imageStream);
            var croppedImage = CropToPaper(image);
            
            // Perform OCR
            var ocrText = await PerformOcrOnImageAsync(croppedImage);

            using var writer = new PdfWriter(outputStream);
            writer.SetCloseStream(false);
            using var pdf = new PdfDocument(writer);
            using var document = new iText.Layout.Document(pdf);

            // Add OCR text to PDF
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                document.Add(new iText.Layout.Element.Paragraph("Extracted Text (OCR)").SetFontSize(14).SetBold());
                document.Add(new iText.Layout.Element.Paragraph(ocrText));
            }
            else
            {
                document.Add(new iText.Layout.Element.Paragraph("No text extracted from image"));
            }

            document.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting image to PDF");
            throw;
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private async Task<string> GenerateSlugFromPdfAsync(Stream pdfStream, string fallback)
    {
        try
        {
            pdfStream.Position = 0;
            using var pdfReader = new PdfReader(pdfStream);
            using var pdfDocument = new PdfDocument(pdfReader);
            
            if (pdfDocument.GetNumberOfPages() > 0)
            {
                var firstPage = pdfDocument.GetFirstPage();
                var text = PdfTextExtractor.GetTextFromPage(firstPage);
                
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Take first line or first few words
                    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                    {
                        var firstLine = lines[0].Trim();
                        var words = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var snippet = string.Join(' ', words.Take(6));
                        if (!string.IsNullOrWhiteSpace(snippet))
                            return Slugify(snippet);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating slug from PDF, using fallback");
        }

        return Slugify(fallback ?? "document");
    }

    private async Task<(MemoryStream markdownStream, string markdownFileName)> ConvertToMarkdownAsync(Stream inputStream, string fileName, string documentType, string? requestId)
    {
        var baseFileName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var markdownFileName = $"{baseFileName}.md";

        MemoryStream convertedStream;
        string convertedName;

        switch (documentType)
        {
            case "pdf":
                (convertedStream, convertedName) = await ConvertPdfToMarkdownAsync(inputStream, markdownFileName, requestId);
                break;

            case "docx":
                (convertedStream, convertedName) = await ConvertDocxToMarkdownAsync(inputStream, markdownFileName, requestId);
                break;

            case "txt":
                (convertedStream, convertedName) = await ConvertTextToMarkdownAsync(inputStream, markdownFileName, requestId);
                break;

            case "xml":
                (convertedStream, convertedName) = await ConvertXmlToMarkdownAsync(inputStream, markdownFileName, requestId);
                break;

            case "image":
                (convertedStream, convertedName) = await ConvertImageToMarkdownAsync(inputStream, markdownFileName, requestId);
                break;

            default:
                throw new NotSupportedException($"Document type {documentType} is not supported");
        }

        // Read converted markdown to decide on a content-based filename
        convertedStream.Position = 0;
        string markdownText;
        using (var reader = new StreamReader(convertedStream, Encoding.UTF8, true, 1024, true))
        {
            markdownText = await reader.ReadToEndAsync();
        }

        // Pick a slug based on first heading or a short snippet; fallback to original base filename
        var slug = GenerateSlugFromMarkdown(markdownText, System.IO.Path.GetFileNameWithoutExtension(markdownFileName));
        var finalFileName = $"{slug}.md";

        // Reset stream position for downstream consumers
        convertedStream.Position = 0;
        return (convertedStream, finalFileName);
    }

    private string GenerateSlugFromMarkdown(string markdown, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            // First, attempt to find a markdown heading
            using (var reader = new StringReader(markdown))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#"))
                    {
                        var title = trimmed.TrimStart('#').Trim();
                        if (!string.IsNullOrWhiteSpace(title))
                            return Slugify(title);
                    }
                }
            }

            // No heading found; take first non-empty line and use first few words
            using (var reader2 = new StringReader(markdown))
            {
                string? line;
                while ((line = reader2.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var snippet = string.Join(' ', words.Take(6));
                    if (!string.IsNullOrWhiteSpace(snippet))
                        return Slugify(snippet);
                }
            }
        }

        return Slugify(fallback ?? "document");
    }

    private string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "document";
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-')
                sb.Append(ch);
        }

        var cleaned = sb.ToString();
        cleaned = Regex.Replace(cleaned, "\\s+", "-");
        cleaned = Regex.Replace(cleaned, "-+", "-");
        cleaned = cleaned.Trim('-').ToLowerInvariant();
        if (string.IsNullOrEmpty(cleaned)) cleaned = "document";
        if (cleaned.Length > 100) cleaned = cleaned.Substring(0, 100);
        return cleaned;
    }

    private async Task<(MemoryStream, string)> ConvertPdfToMarkdownAsync(Stream pdfStream, string markdownFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting PDF to Markdown", 20);
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"# {System.IO.Path.GetFileNameWithoutExtension(markdownFileName)}");
        markdown.AppendLine();

        try
        {
            using var pdfReader = new PdfReader(pdfStream);
            using var pdfDocument = new PdfDocument(pdfReader);
            var totalPages = pdfDocument.GetNumberOfPages();

            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                var page = pdfDocument.GetPage(pageNum);
                var strategy = new LocationTextExtractionStrategy();
                string pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                if (totalPages > 1)
                {
                    markdown.AppendLine($"## Page {pageNum}");
                    markdown.AppendLine();
                }

                markdown.AppendLine(pageText);
                markdown.AppendLine();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting PDF to Markdown");
            throw;
        }

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(markdown.ToString()));
        return (ms, markdownFileName);
    }

    private async Task<(MemoryStream, string)> ConvertDocxToMarkdownAsync(Stream docxStream, string markdownFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting DOCX to Markdown", 20);
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"# {System.IO.Path.GetFileNameWithoutExtension(markdownFileName)}");
        markdown.AppendLine();

        try
        {
            using var doc = WordprocessingDocument.Open(docxStream, false);
            var body = doc.MainDocumentPart?.Document?.Body;

            if (body != null)
            {
                foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                {
                    var text = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Check if it's a heading based on style
                        var pPr = paragraph.Elements<ParagraphProperties>().FirstOrDefault();
                        var styleId = pPr?.ParagraphStyleId?.Val?.Value;
                        
                        if (styleId != null && styleId.StartsWith("Heading"))
                        {
                            // Try to extract heading level, default to 1 if not numeric
                            var levelStr = styleId.Substring(7);
                            int level = 1;
                            if (!string.IsNullOrEmpty(levelStr) && int.TryParse(levelStr, out var parsedLevel))
                            {
                                level = parsedLevel;
                            }
                            markdown.AppendLine($"{new string('#', Math.Min(level, 6))} {text}");
                        }
                        else
                        {
                            markdown.AppendLine(text);
                        }
                        markdown.AppendLine();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting DOCX to Markdown");
            throw;
        }

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(markdown.ToString()));
        return (ms, markdownFileName);
    }

    private async Task<(MemoryStream, string)> ConvertTextToMarkdownAsync(Stream textStream, string markdownFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting text to Markdown", 20);
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"# {System.IO.Path.GetFileNameWithoutExtension(markdownFileName)}");
        markdown.AppendLine();

        using var reader = new StreamReader(textStream);
        var text = await reader.ReadToEndAsync();
        markdown.AppendLine(text);

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(markdown.ToString()));
        return (ms, markdownFileName);
    }

    private async Task<(MemoryStream, string)> ConvertXmlToMarkdownAsync(Stream xmlStream, string markdownFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting XML to Markdown", 20);
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"# {System.IO.Path.GetFileNameWithoutExtension(markdownFileName)}");
        markdown.AppendLine();

        try
        {
            using var reader = new StreamReader(xmlStream);
            var xmlContent = await reader.ReadToEndAsync();
            
            // Parse XML and convert to readable markdown
            var xdoc = XDocument.Parse(xmlContent);
            
            markdown.AppendLine("## XML Content");
            markdown.AppendLine();
            markdown.AppendLine("```xml");
            markdown.AppendLine(xdoc.ToString());
            markdown.AppendLine("```");
            markdown.AppendLine();
            
            // Also extract text content for better searchability
            markdown.AppendLine("## Text Content");
            markdown.AppendLine();
            ExtractXmlText(xdoc.Root, markdown, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting XML to Markdown");
            // Fallback: treat as plain text
            xmlStream.Position = 0;
            using var reader = new StreamReader(xmlStream);
            var content = await reader.ReadToEndAsync();
            markdown.AppendLine("```xml");
            markdown.AppendLine(content);
            markdown.AppendLine("```");
        }

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(markdown.ToString()));
        return (ms, markdownFileName);
    }

    private void ExtractXmlText(XElement? element, StringBuilder markdown, int level)
    {
        if (element == null) return;

        var indent = new string(' ', level * 2);
        
        // Add element name as heading or list item
        if (!string.IsNullOrWhiteSpace(element.Value) && !element.HasElements)
        {
            markdown.AppendLine($"{indent}- **{element.Name.LocalName}**: {element.Value.Trim()}");
        }
        else if (element.HasElements)
        {
            markdown.AppendLine($"{indent}- **{element.Name.LocalName}**");
        }

        // Process child elements
        foreach (var child in element.Elements())
        {
            ExtractXmlText(child, markdown, level + 1);
        }
    }

    private async Task<(MemoryStream, string)> ConvertImageToMarkdownAsync(Stream imageStream, string markdownFileName, string? requestId)
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

        // Create Markdown with embedded image and OCR text
        var markdown = new StringBuilder();
        markdown.AppendLine($"# {System.IO.Path.GetFileNameWithoutExtension(markdownFileName)}");
        markdown.AppendLine();
        markdown.AppendLine("## Image Content");
        markdown.AppendLine();
        
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            markdown.AppendLine("### Extracted Text (OCR)");
            markdown.AppendLine();
            markdown.AppendLine(ocrText);
            markdown.AppendLine();
        }
        else
        {
            markdown.AppendLine("*No text extracted from image*");
            markdown.AppendLine();
        }

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(markdown.ToString()));
        return (ms, markdownFileName);
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

    private async Task<List<DocumentChunk>> ExtractFromPdfWithLocationAsync(Stream pdfStream, Guid documentId, string? requestId)
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
                var page = pdfDocument.GetPage(pageNum);
                var pageSize = page.GetPageSize();
                
                // Extract text with location using location-aware extraction strategy
                var strategy = new SimpleTextWithLocationStrategy();
                PdfTextExtractor.GetTextFromPage(page, strategy);
                var textItems = strategy.GetTextItems();

                if (textItems.Any())
                {
                    // Create chunks from text items with actual location data
                    var pageChunks = CreateTextChunksFromTextItems(textItems, pageNum, documentId, ref chunkIndex);
                    chunks.AddRange(pageChunks);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text with location from PDF");
            throw;
        }

        return chunks;
    }

    private List<DocumentChunk> CreateTextChunksFromTextItems(List<TextItem> textItems, int pageNumber, Guid documentId, ref int chunkIndex)
    {
        var chunks = new List<DocumentChunk>();

        // Combine all text items into a single text string
        var fullText = string.Join(" ", textItems.Select(t => t.Text));
        
        if (string.IsNullOrWhiteSpace(fullText))
            return chunks;

        var startIndex = 0;
        var currentPosition = 0;

        while (startIndex < fullText.Length)
        {
            var endIndex = Math.Min(startIndex + ChunkSize, fullText.Length);

            // Try to break at sentence or word boundary
            if (endIndex < fullText.Length)
            {
                var lastPeriod = fullText.LastIndexOf('.', endIndex, Math.Min(100, endIndex - startIndex));
                var lastSpace = fullText.LastIndexOf(' ', endIndex, Math.Min(100, endIndex - startIndex));

                if (lastPeriod > startIndex)
                    endIndex = lastPeriod + 1;
                else if (lastSpace > startIndex)
                    endIndex = lastSpace;
            }

            var chunkText = fullText.Substring(startIndex, endIndex - startIndex).Trim();

            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                // Find the text items that correspond to this chunk
                var chunkItems = new List<TextItem>();
                var chunkCharCount = 0;
                
                for (int i = currentPosition; i < textItems.Count && chunkCharCount < chunkText.Length; i++)
                {
                    chunkItems.Add(textItems[i]);
                    chunkCharCount += textItems[i].Text.Length + 1; // +1 for space
                }

                // Calculate bounding box from text items
                double minX = chunkItems.Any() ? chunkItems.Min(t => t.X) : 0;
                double maxX = chunkItems.Any() ? chunkItems.Max(t => t.X + t.Width) : 0;
                double minY = chunkItems.Any() ? chunkItems.Min(t => t.Y) : 0;
                double maxY = chunkItems.Any() ? chunkItems.Max(t => t.Y + t.Height) : 0;

                var chunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentMetadataId = documentId,
                    ChunkText = chunkText,
                    PageNumber = pageNumber,
                    ChunkIndex = chunkIndex++,
                    X = minX,
                    Y = minY,
                    Width = maxX - minX,
                    Height = maxY - minY,
                    CreatedAt = DateTime.UtcNow
                };

                chunks.Add(chunk);
                currentPosition += chunkItems.Count;
            }

            // Ensure we always advance to prevent infinite loops
            var nextStart = endIndex - ChunkOverlap;
            startIndex = Math.Max(nextStart, startIndex + 1);
        }

        return chunks;
    }

    private async Task<List<DocumentChunk>> ExtractFromMarkdownAsync(Stream markdownStream, Guid documentId, string? requestId)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        try
        {
            using var reader = new StreamReader(markdownStream);
            var markdownText = await reader.ReadToEndAsync();

            // Create chunks from markdown text
            var pageChunks = CreateTextChunks(markdownText, 1, documentId, ref chunkIndex);
            chunks.AddRange(pageChunks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from Markdown");
            throw;
        }

        return chunks;
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

        // Re-extract from the stored PDF with location data
        var chunks = await ExtractFromPdfWithLocationAsync(stream, metadata.Id, null);

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
            ".xml" => "xml",
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tiff" or ".gif" => "image",
            _ => contentType.StartsWith("image/") ? "image" : 
                 contentType.Contains("xml") ? "xml" : 
                 contentType.Contains("pdf") ? "pdf" : "unknown"
        };
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

/// <summary>
/// Helper class to store text with its position information
/// </summary>
public class TextItem
{
    public string Text { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Custom text extraction strategy that captures text positions
/// </summary>
public class SimpleTextWithLocationStrategy : ITextExtractionStrategy
{
    private readonly List<TextItem> _textItems = new();

    public void EventOccurred(IEventData data, EventType type)
    {
        if (type == EventType.RENDER_TEXT)
        {
            var renderInfo = (TextRenderInfo)data;
            var text = renderInfo.GetText();
            
            if (!string.IsNullOrWhiteSpace(text))
            {
                var baseline = renderInfo.GetBaseline();
                var ascentLine = renderInfo.GetAscentLine();
                
                _textItems.Add(new TextItem
                {
                    Text = text,
                    X = baseline.GetStartPoint().Get(0),
                    Y = baseline.GetStartPoint().Get(1),
                    Width = baseline.GetLength(),
                    Height = ascentLine.GetStartPoint().Get(1) - baseline.GetStartPoint().Get(1)
                });
            }
        }
    }

    public ICollection<EventType> GetSupportedEvents()
    {
        return new[] { EventType.RENDER_TEXT };
    }

    public string GetResultantText()
    {
        return string.Join(" ", _textItems.Select(t => t.Text));
    }

    public List<TextItem> GetTextItems()
    {
        return _textItems;
    }
}
