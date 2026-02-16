namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Constants for PDF document processing.
/// Note: These assume US Letter size (8.5" x 11"). For other page sizes like A4 (210mm x 297mm),
/// consider implementing dynamic page size detection from the actual PDF page dimensions.
/// </summary>
public static class PdfConstants
{
    /// <summary>
    /// Standard US Letter page width in points (8.5 inches * 72 points/inch)
    /// </summary>
    public const double PageWidth = 612;

    /// <summary>
    /// Standard US Letter page height in points (11 inches * 72 points/inch)
    /// </summary>
    public const double PageHeight = 792;
}
