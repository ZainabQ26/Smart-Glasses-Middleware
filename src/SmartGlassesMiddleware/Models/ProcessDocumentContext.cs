namespace SmartGlassesMiddleware.Models;

public class ProcessDocumentContext
{
    public string? DocumentName { get; set; }
    public int? CurrentPage { get; set; }
    public int? TotalPages { get; set; }
    public List<ProcessDocumentPageContext> Pages { get; set; } = new();
    public List<ProcessDocumentHighlightContext> Highlights { get; set; } = new();
}

public class ProcessDocumentPageContext
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class ProcessDocumentHighlightContext
{
    public string Keyword { get; set; } = string.Empty;
    public List<int> Pages { get; set; } = new();
}