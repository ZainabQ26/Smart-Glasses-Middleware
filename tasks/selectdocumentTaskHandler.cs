using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;

public class SelectDocumentTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;

    public string Name => "select-document";

    public SelectDocumentTaskHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> HandleAsync(string input)
    {
        var lastDoc = await _context.DocumentMetadatas
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastDoc == null)
            return "No documents found. Please upload a file first.";

        // Return a special action token the frontend intercepts to open the file preview
        return $"__ACTION:SELECT_DOCUMENT:{lastDoc.Id}:{lastDoc.OriginalFileName}";
    }
}