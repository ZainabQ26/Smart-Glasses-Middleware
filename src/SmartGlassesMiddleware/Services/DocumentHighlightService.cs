using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Colors;
using iText.Kernel.Geom;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SmartGlassesMiddleware.Services;

public interface IDocumentHighlightService
{
    Task<Stream> HighlightDocumentAsync(Guid documentId, List<Guid> chunkIds);
    Task<Stream> HighlightTextLocationsAsync(Guid documentId, string searchText);
}

public class DocumentHighlightService : IDocumentHighlightService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DocumentHighlightService> _logger;
    private readonly IMediaStorageService _storageService;

    public DocumentHighlightService(
        ApplicationDbContext context,
        ILogger<DocumentHighlightService> logger,
        IMediaStorageService storageService)
    {
        _context = context;
        _logger = logger;
        _storageService = storageService;
    }

    public async Task<Stream> HighlightDocumentAsync(Guid documentId, List<Guid> chunkIds)
    {
        var document = await _context.DocumentMetadatas
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            throw new FileNotFoundException($"Document {documentId} not found");

        var chunksToHighlight = document.Chunks
            .Where(c => chunkIds.Contains(c.Id))
            .ToList();

        // Get original document from storage
        var originalStream = await _storageService.DownloadFileAsync(document.StorageKey);

        // Create highlighted copy based on document type
        return document.DocumentType switch
        {
            "pdf" => await HighlightPdfAsync(originalStream, chunksToHighlight),
            "image" => await HighlightImageAsync(originalStream, chunksToHighlight),
            _ => throw new NotSupportedException($"Highlighting not supported for {document.DocumentType}")
        };
    }

    public async Task<Stream> HighlightTextLocationsAsync(Guid documentId, string searchText)
    {
        var document = await _context.DocumentMetadatas
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            throw new FileNotFoundException($"Document {documentId} not found");

        // Find chunks containing the search text
        var relevantChunks = document.Chunks
            .Where(c => c.ChunkText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!relevantChunks.Any())
        {
            _logger.LogInformation("No chunks found containing text: {SearchText}", searchText);
            // Return original document without highlights
            return await _storageService.DownloadFileAsync(document.StorageKey);
        }

        return await HighlightDocumentAsync(documentId, relevantChunks.Select(c => c.Id).ToList());
    }

    private async Task<Stream> HighlightPdfAsync(Stream originalPdfStream, List<DocumentChunk> chunks)
    {
        var outputStream = new MemoryStream();

        try
        {
            using var reader = new PdfReader(originalPdfStream);
            using var writer = new PdfWriter(outputStream);
            writer.SetCloseStream(false); // Keep stream open for return
            using var pdfDoc = new PdfDocument(reader, writer);

            // Group chunks by page
            var chunksByPage = chunks.GroupBy(c => c.PageNumber);

            foreach (var pageGroup in chunksByPage)
            {
                var pageNum = pageGroup.Key;
                if (pageNum < 1 || pageNum > pdfDoc.GetNumberOfPages())
                    continue;

                var page = pdfDoc.GetPage(pageNum);
                var canvas = new PdfCanvas(page);

                foreach (var chunk in pageGroup)
                {
                    // If we have positional data, highlight that area
                    if (chunk.X.HasValue && chunk.Y.HasValue && chunk.Width.HasValue && chunk.Height.HasValue)
                    {
                        HighlightArea(canvas, chunk.X.Value, chunk.Y.Value, chunk.Width.Value, chunk.Height.Value);
                    }
                    else
                    {
                        // Try to find text on page and highlight it
                        await HighlightTextOnPageAsync(canvas, page, chunk.ChunkText);
                    }
                }
            }

            pdfDoc.Close();
            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error highlighting PDF");
            outputStream.Dispose();
            throw;
        }
    }

    private void HighlightArea(PdfCanvas canvas, double x, double y, double width, double height)
    {
        canvas.SaveState();
        canvas.SetFillColor(ColorConstants.YELLOW);
        canvas.SetExtGState(new iText.Kernel.Pdf.Extgstate.PdfExtGState().SetFillOpacity(0.3f));
        canvas.Rectangle(x, y, width, height);
        canvas.Fill();
        canvas.RestoreState();
    }

    private async Task HighlightTextOnPageAsync(PdfCanvas canvas, PdfPage page, string text)
    {
        // This is a simplified implementation
        // A full implementation would parse the page content to find exact text locations
        // For now, we'll add a note that highlighting is approximate
        _logger.LogInformation("Highlighting text without exact coordinates: {Text}", text.Substring(0, Math.Min(50, text.Length)));

        // Could implement text location finding using iText's LocationTextExtractionStrategy
        // For simplicity, we're noting this as a limitation
    }

    private async Task<Stream> HighlightImageAsync(Stream originalImageStream, List<DocumentChunk> chunks)
    {
        var outputStream = new MemoryStream();

        try
        {
            using var image = await SixLabors.ImageSharp.Image.LoadAsync(originalImageStream);

            image.Mutate(ctx =>
            {
                foreach (var chunk in chunks)
                {
                    if (chunk.X.HasValue && chunk.Y.HasValue && chunk.Width.HasValue && chunk.Height.HasValue)
                    {
                        // Draw semi-transparent yellow rectangle
                        var rect = new RectangleF(
                            (float)chunk.X.Value,
                            (float)chunk.Y.Value,
                            (float)chunk.Width.Value,
                            (float)chunk.Height.Value
                        );

                        var color = SixLabors.ImageSharp.Color.FromRgba(255, 255, 0, 128); // Yellow with 50% opacity
                        ctx.Fill(color, rect);
                    }
                }
            });

            await image.SaveAsPngAsync(outputStream);
            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error highlighting image");
            outputStream.Dispose();
            throw;
        }
    }
}
