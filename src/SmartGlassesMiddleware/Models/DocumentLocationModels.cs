namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Request to find the location of specific text in a document
/// </summary>
public class FindLocationRequest
{
    public string SearchText { get; set; } = string.Empty;
}

/// <summary>
/// Response containing the location of found text
/// </summary>
public class TextLocation
{
    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public Guid ChunkId { get; set; }
    public int PageNumber { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string Position { get; set; } = string.Empty; // e.g., "bottom right of page 2"
    public string ContextText { get; set; } = string.Empty;
}

/// <summary>
/// Response containing all locations where specific text was found
/// </summary>
public class FindLocationResponse
{
    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public int MatchCount { get; set; }
    public List<TextLocation> Locations { get; set; } = new();
}
