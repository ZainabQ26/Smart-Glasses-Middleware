using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Tesseract;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Geom;

namespace SmartGlassesMiddleware.Services;

public class OcrWordBox
{
    public string Text { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class PageOcrData
{
    public int PageIndex { get; set; }
    public byte[] OriginalImageBytes { get; set; } = Array.Empty<byte>();
    public byte[] PreprocessedImageBytes { get; set; } = Array.Empty<byte>();
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public List<OcrWordBox> Words { get; set; } = new();
}

public interface IOcrBatchService
{
    Task<PageOcrData> ProcessSinglePageAsync(byte[] imageBytes, int pageIndex);
    Task<byte[]> GenerateSearchablePdfAsync(List<PageOcrData> pages);
}

public class OcrBatchService : IOcrBatchService
{
    private readonly ILogger<OcrBatchService> _logger;
    private readonly string _tessDataPath;

    public OcrBatchService(ILogger<OcrBatchService> logger)
    {
        _logger = logger;

        // Resolve tessdata directory
        var candidates = new[]
        {
            "/usr/share/tesseract-ocr/5/tessdata",
            "/usr/share/tessdata",
            "/app/tessdata",
            System.IO.Path.Combine(AppContext.BaseDirectory, "tessdata"),
            "./tessdata"
        };

        _tessDataPath = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        _logger.LogInformation("OcrBatchService initialized with tessdata path: {Path}", _tessDataPath);
    }

    public Task<PageOcrData> ProcessSinglePageAsync(byte[] imageBytes, int pageIndex)
    {
        return Task.Run(() =>
        {
            var pageData = new PageOcrData
            {
                PageIndex = pageIndex,
                OriginalImageBytes = imageBytes
            };

            // 1. Image Preprocessing with SixLabors.ImageSharp
            byte[] ocrReadyBytes;
            using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes))
            {
                // Auto-orient based on EXIF metadata
                image.Mutate(ctx => ctx.AutoOrient());

                // Cap max dimension to 2400 to prevent excessive memory usage
                int maxDim = Math.Max(image.Width, image.Height);
                if (maxDim > 2400)
                {
                    double scale = 2400.0 / maxDim;
                    image.Mutate(ctx => ctx.Resize((int)(image.Width * scale), (int)(image.Height * scale)));
                }

                pageData.ImageWidth = image.Width;
                pageData.ImageHeight = image.Height;

                // Create enhanced image for OCR: Grayscale + Contrast + Sharpen
                using var ocrImage = image.Clone(ctx =>
                {
                    ctx.Grayscale()
                       .Contrast(1.35f)
                       .GaussianSharpen(0.75f);
                });

                using var ms = new MemoryStream();
                ocrImage.SaveAsPng(ms);
                ocrReadyBytes = ms.ToArray();
                pageData.PreprocessedImageBytes = ocrReadyBytes;
            }

            // 2. OCR with Tesseract
            try
            {
                using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
                using var pix = Pix.LoadFromMemory(ocrReadyBytes);
                using var page = engine.Process(pix);

                pageData.ExtractedText = page.GetText()?.Trim() ?? string.Empty;

                using var iter = page.GetIterator();
                iter.Begin();

                do
                {
                    if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out Rect rect))
                    {
                        var wordText = iter.GetText(PageIteratorLevel.Word)?.Trim();
                        if (!string.IsNullOrEmpty(wordText))
                        {
                            pageData.Words.Add(new OcrWordBox
                            {
                                Text = wordText,
                                X = rect.X1,
                                Y = rect.Y1,
                                Width = rect.Width,
                                Height = rect.Height
                            });
                        }
                    }
                } while (iter.Next(PageIteratorLevel.Word));

                _logger.LogInformation("Page {PageIndex} OCR complete: {WordCount} words extracted", pageIndex, pageData.Words.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tesseract OCR failed on page {PageIndex}", pageIndex);
            }

            return pageData;
        });
    }

    public Task<byte[]> GenerateSearchablePdfAsync(List<PageOcrData> pages)
    {
        return Task.Run(() =>
        {
            using var outputStream = new MemoryStream();
            using var writer = new PdfWriter(outputStream);
            using var pdfDoc = new PdfDocument(writer);

            var standardFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            foreach (var pageData in pages)
            {
                float width = pageData.ImageWidth > 0 ? pageData.ImageWidth : 1280;
                float height = pageData.ImageHeight > 0 ? pageData.ImageHeight : 720;
                var pageSize = new PageSize(width, height);
                var pdfPage = pdfDoc.AddNewPage(pageSize);
                var canvas = new PdfCanvas(pdfPage);

                var imgData = ImageDataFactory.Create(pageData.OriginalImageBytes);
                canvas.AddImageWithTransformationMatrix(imgData, width, 0, 0, height, 0, 0, false);

                if (pageData.Words.Count > 0)
                {
                    canvas.BeginText();
                    canvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE);

                    foreach (var word in pageData.Words)
                    {
                        if (string.IsNullOrWhiteSpace(word.Text)) continue;

                        float pdfX = word.X;
                        float pdfY = height - word.Y - word.Height;
                        float fontSize = Math.Max(word.Height, 8);

                        canvas.SetFontAndSize(standardFont, fontSize);
                        canvas.MoveText(pdfX, pdfY);
                        canvas.ShowText(word.Text);
                        canvas.MoveText(-pdfX, -pdfY);
                    }

                    canvas.EndText();
                }
            }

            pdfDoc.Close();
            return outputStream.ToArray();
        });
    }
}