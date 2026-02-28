using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System.Linq;

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
        // Try ManagedFiles first (direct uploads via admin panel)
        var lastFile = await _context.ManagedFiles
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastFile != null)
            return $"__ACTION:SELECT_DOCUMENT:{lastFile.Id}:{lastFile.FileName}";

        // Fall back to DocumentMetadatas (processed/converted documents)
        var lastDoc = await _context.DocumentMetadatas
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastDoc == null)
            return "No documents found. Please upload a file first.";

        // Use ManagedFileId if available so the preview endpoint works correctly
        var previewId = lastDoc.ManagedFileId.HasValue ? lastDoc.ManagedFileId.Value.ToString() : lastDoc.Id.ToString();
        return $"__ACTION:SELECT_DOCUMENT:{previewId}:{lastDoc.OriginalFileName}";
    }
}