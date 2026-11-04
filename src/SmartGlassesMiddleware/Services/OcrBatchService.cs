using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Normalization;
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

    private const int MaxLongSide = 3200;
    private const int TargetLongSide = 2800;

    private static (string Text, List<OcrWordBox> Words, double Score) RunOcr(TesseractEngine engine, byte[] pngBytes, double ocrScale)
    {
        var words = new List<OcrWordBox>();
        double score = 0;

        using var pix = Pix.LoadFromMemory(pngBytes);
        using var page = engine.Process(pix);
        var text = page.GetText()?.Trim() ?? string.Empty;

        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out Rect rect)) continue;
            var wordText = iter.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrEmpty(wordText)) continue;

            float conf = iter.GetConfidence(PageIteratorLevel.Word);
            if (conf >= 30) score += conf;

            // Map OCR-image coordinates back to PDF-page coordinates
            words.Add(new OcrWordBox
            {
                Text = wordText,
                X = (int)(rect.X1 / ocrScale),
                Y = (int)(rect.Y1 / ocrScale),
                Width = Math.Max(1, (int)(rect.Width / ocrScale)),
                Height = Math.Max(1, (int)(rect.Height / ocrScale))
            });
        } while (iter.Next(PageIteratorLevel.Word));

        return (text, words, score);
    }

    /// <summary>
    /// Estimates page skew in degrees (positive = text slopes clockwise) by maximising the
    /// variance of horizontal ink projections over a small downscaled binary copy.
    /// </summary>
    private static double EstimateSkewDegrees(SixLabors.ImageSharp.Image<Rgba32> source)
    {
        const int probeWidth = 640;
        double scale = Math.Min(1.0, (double)probeWidth / source.Width);
        using var probe = source.Clone(ctx =>
        {
            ctx.Resize((int)(source.Width * scale), (int)(source.Height * scale)).Grayscale().AdaptiveThreshold();
        });

        double bestAngle = 0, bestScore = double.MinValue;
        for (double angle = -10; angle <= 10.0001; angle += 0.5)
        {
            using var rotated = probe.Clone(ctx => ctx.Rotate((float)angle).BackgroundColor(Color.White));
            var rows = new double[rotated.Height];
            rotated.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int dark = 0;
                    for (int x = 0; x < row.Length; x++)
                        if (row[x].R < 128) dark++;
                    rows[y] = dark;
                }
            });

            double mean = rows.Average();
            double variance = rows.Sum(r => (r - mean) * (r - mean));
            if (variance > bestScore) { bestScore = variance; bestAngle = angle; }
        }
        return bestAngle;
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

            // 1. Image preprocessing with SixLabors.ImageSharp
            byte[] enhancedBytes;
            byte[] binarizedBytes;
            double ocrScale = 1.0; // OCR-image pixels per PDF-page pixel (word boxes are divided by this)

            using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes))
            {
                // Auto-orient based on EXIF metadata
                image.Mutate(ctx => ctx.AutoOrient());

                // Cap very large images to limit memory; 3200px keeps small print legible
                int maxDim = Math.Max(image.Width, image.Height);
                if (maxDim > MaxLongSide)
                {
                    double down = (double)MaxLongSide / maxDim;
                    image.Mutate(ctx => ctx.Resize((int)(image.Width * down), (int)(image.Height * down)));
                }

                // Deskew (projection-profile search). Applied to the page image itself so the
                // searchable-PDF text layer stays aligned with what is embedded.
                double skew = EstimateSkewDegrees(image);
                if (Math.Abs(skew) >= 0.5)
                {
                    image.Mutate(ctx => ctx.Rotate((float)skew).BackgroundColor(Color.White));
                    using var deskewed = new MemoryStream();
                    image.SaveAsJpeg(deskewed, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 92 });
                    pageData.OriginalImageBytes = deskewed.ToArray();
                    _logger.LogInformation("Page {PageIndex} deskewed by {Skew:F1} degrees", pageIndex, skew);
                }

                pageData.ImageWidth = image.Width;
                pageData.ImageHeight = image.Height;

                // Upscale small captures so glyphs reach ~30px x-height (Tesseract's sweet spot)
                int longSide = Math.Max(image.Width, image.Height);
                int targetW = image.Width, targetH = image.Height;
                if (longSide < TargetLongSide)
                {
                    ocrScale = (double)TargetLongSide / longSide;
                    targetW = (int)(image.Width * ocrScale);
                    targetH = (int)(image.Height * ocrScale);
                }

                // Variant A: grayscale + adaptive (CLAHE) contrast. Handles uneven lighting without
                // the clipped highlights a global Contrast() causes.
                using var enhanced = image.Clone(ctx =>
                {
                    if (ocrScale > 1.0)
                        ctx.Resize(new ResizeOptions { Size = new SixLabors.ImageSharp.Size(targetW, targetH), Sampler = KnownResamplers.Lanczos3 });
                    ctx.Grayscale()
                       .HistogramEqualization(new HistogramEqualizationOptions
                       {
                           Method = HistogramEqualizationMethod.AdaptiveTileInterpolation,
                           ClipHistogram = true,
                           NumberOfTiles = 8
                       });
                });

                // Variant B: denoise + adaptive (Bradley) binarization. Rescues shadowed regions.
                using var binarized = enhanced.Clone(ctx => ctx.GaussianBlur(0.7f).AdaptiveThreshold());

                using (var ms = new MemoryStream()) { enhanced.SaveAsPng(ms); enhancedBytes = ms.ToArray(); }
                using (var ms = new MemoryStream()) { binarized.SaveAsPng(ms); binarizedBytes = ms.ToArray(); }
                pageData.PreprocessedImageBytes = enhancedBytes;
            }

            // 2. OCR with Tesseract: run both variants, keep whichever reads more confident words
            try
            {
                using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
                engine.DefaultPageSegMode = PageSegMode.Auto;          // full automatic layout analysis
                engine.SetVariable("user_defined_dpi", "300");         // camera JPEGs carry no DPI metadata
                engine.SetVariable("preserve_interword_spaces", "1");

                var a = RunOcr(engine, enhancedBytes, ocrScale);
                var b = RunOcr(engine, binarizedBytes, ocrScale);
                var best = b.Score > a.Score ? b : a;

                pageData.ExtractedText = best.Text;
                pageData.Words = best.Words;

                _logger.LogInformation(
                    "Page {PageIndex} OCR complete: {WordCount} words (enhanced score {A:F0}, binarized score {B:F0})",
                    pageIndex, pageData.Words.Count, a.Score, b.Score);
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