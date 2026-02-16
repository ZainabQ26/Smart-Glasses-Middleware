using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Geom;
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
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Holds location metadata for document text chunks
/// </summary>
public class TextLocationInfo
{
    public int PageNumber { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Metadata container for document location information
/// </summary>
public class DocumentLocationMetadata
{
    public List<TextLocationInfo> Locations { get; set; } = new();
}

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
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", $"Converting {fileName} to Markdown", 10);
        }

        // Convert to Markdown and get the converted stream with location data
        MemoryStream markdownStream;
        string markdownFileName;
        DocumentLocationMetadata? locationMetadata = null;
        
        try
        {
            fileStream.Position = 0;
            (markdownStream, markdownFileName, locationMetadata) = await ConvertToMarkdownAsync(fileStream, fileName, documentType, requestId);
            
            if (requestId != null && _monitoringService != null)
            {
                await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Uploading converted Markdown", 40);
            }

            // Store the converted Markdown
            var storageKey = $"documents/{documentId}/{markdownFileName}";
            markdownStream.Position = 0;
            await _storageService.UploadFileAsync(storageKey, markdownStream, "text/markdown");

            // Store location metadata if available
            if (locationMetadata != null && locationMetadata.Locations.Any())
            {
                var locationJson = JsonSerializer.Serialize(locationMetadata);
                var locationStream = new MemoryStream(Encoding.UTF8.GetBytes(locationJson));
                var locationKey = $"documents/{documentId}/location-metadata.json";
                await _storageService.UploadFileAsync(locationKey, locationStream, "application/json");
            }

            var metadata = new DocumentMetadata
            {
                Id = documentId,
                FileName = markdownFileName,
                OriginalFileName = fileName,
                ContentType = "text/markdown",
                StorageKey = storageKey,
                UploadedAt = DateTime.UtcNow,
                FileSize = markdownStream.Length,
                DocumentType = "markdown",
                IsProcessed = false
            };

            if (requestId != null && _monitoringService != null)
            {
                await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Extracting text from Markdown", 60);
            }

            // Extract text and create chunks from the Markdown with location data
            markdownStream.Position = 0;
            var chunks = await ExtractFromMarkdownAsync(markdownStream, metadata.Id, requestId, locationMetadata);

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

    private async Task<(MemoryStream markdownStream, string markdownFileName, DocumentLocationMetadata? locationMetadata)> ConvertToMarkdownAsync(Stream inputStream, string fileName, string documentType, string? requestId)
    {
        var baseFileName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var markdownFileName = $"{baseFileName}.md";

        MemoryStream convertedStream;
        string convertedName;
        DocumentLocationMetadata? locationMetadata = null;

        switch (documentType)
        {
            case "pdf":
                (convertedStream, convertedName, locationMetadata) = await ConvertPdfToMarkdownAsync(inputStream, markdownFileName, requestId);
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
        return (convertedStream, finalFileName, locationMetadata);
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

    private async Task<(MemoryStream, string, DocumentLocationMetadata)> ConvertPdfToMarkdownAsync(Stream pdfStream, string markdownFileName, string? requestId)
    {
        if (requestId != null && _monitoringService != null)
        {
            await _monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting PDF to Markdown", 20);
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"# {System.IO.Path.GetFileNameWithoutExtension(markdownFileName)}");
        markdown.AppendLine();

        var locationMetadata = new DocumentLocationMetadata();

        try
        {
            using var pdfReader = new PdfReader(pdfStream);
            using var pdfDocument = new PdfDocument(pdfReader);
            var totalPages = pdfDocument.GetNumberOfPages();
            int currentTextIndex = 0;

            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                var page = pdfDocument.GetPage(pageNum);
                
                // Use LocationTextExtractionStrategy to get text with positions
                var strategy = new LocationTextExtractionStrategy();
                string pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                // Get the page size for calculating bounding boxes
                var pageSize = page.GetPageSize();
                var pageHeight = pageSize.GetHeight();
                var pageWidth = pageSize.GetWidth();

                if (totalPages > 1)
                {
                    markdown.AppendLine($"## Page {pageNum}");
                    markdown.AppendLine();
                }

                // Store location info for this page's text
                // Note: For simplicity, we're storing page-level bounding boxes
                // A more sophisticated implementation could extract word-level positions
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    locationMetadata.Locations.Add(new TextLocationInfo
                    {
                        PageNumber = pageNum,
                        StartIndex = currentTextIndex,
                        EndIndex = currentTextIndex + pageText.Length,
                        X = 0,
                        Y = 0,
                        Width = pageWidth,
                        Height = pageHeight
                    });
                    currentTextIndex += pageText.Length + Environment.NewLine.Length * 2; // Account for newlines
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
        return (ms, markdownFileName, locationMetadata);
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

    private async Task<List<DocumentChunk>> ExtractFromMarkdownAsync(Stream markdownStream, Guid documentId, string? requestId, DocumentLocationMetadata? locationMetadata = null)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        try
        {
            using var reader = new StreamReader(markdownStream);
            var markdownText = await reader.ReadToEndAsync();

            // Create chunks from markdown text with location data
            var pageChunks = CreateTextChunks(markdownText, 1, documentId, ref chunkIndex, locationMetadata);
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

        // Try to load location metadata if it exists
        DocumentLocationMetadata? locationMetadata = null;
        try
        {
            var locationKey = $"documents/{metadata.Id}/location-metadata.json";
            using var locationStream = await _storageService.DownloadFileAsync(locationKey);
            using var reader = new StreamReader(locationStream);
            var locationJson = await reader.ReadToEndAsync();
            locationMetadata = JsonSerializer.Deserialize<DocumentLocationMetadata>(locationJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No location metadata found for document {DocumentId}, continuing without it", documentId);
        }

        // Re-extract from the stored Markdown with location data
        var chunks = await ExtractFromMarkdownAsync(stream, metadata.Id, null, locationMetadata);

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
                 contentType.Contains("xml") ? "xml" : "unknown"
        };
    }

    private List<DocumentChunk> CreateTextChunks(string text, int pageNumber, Guid documentId, ref int chunkIndex, DocumentLocationMetadata? locationMetadata = null)
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

                // Try to find location data for this chunk
                if (locationMetadata != null && locationMetadata.Locations.Any())
                {
                    var locationInfo = locationMetadata.Locations
                        .FirstOrDefault(l => startIndex >= l.StartIndex && startIndex < l.EndIndex);
                    
                    if (locationInfo != null)
                    {
                        chunk.PageNumber = locationInfo.PageNumber;
                        chunk.X = locationInfo.X;
                        chunk.Y = locationInfo.Y;
                        chunk.Width = locationInfo.Width;
                        chunk.Height = locationInfo.Height;
                    }
                }

                chunks.Add(chunk);
            }

            // Ensure we always advance to prevent infinite loops
            var nextStart = endIndex - ChunkOverlap;
            startIndex = Math.Max(nextStart, startIndex + 1);
        }

        return chunks;
    }
}
