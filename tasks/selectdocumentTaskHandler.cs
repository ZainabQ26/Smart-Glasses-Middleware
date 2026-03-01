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
        // DocumentMetadatas holds all user-uploaded processed documents.
        // Use OriginalFileName to show the user-friendly name.
        // The preview endpoint now accepts both ManagedFile IDs and DocumentMetadata IDs.
        var lastDoc = await _context.DocumentMetadatas
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastDoc != null)
        {
            // Prefer ManagedFileId so the preview endpoint hits ManagedFiles first,
            // but fall back to the DocumentMetadata ID (preview endpoint now handles both).
            var previewId = lastDoc.ManagedFileId.HasValue
                ? lastDoc.ManagedFileId.Value.ToString()
                : lastDoc.Id.ToString();
            return $"__ACTION:SELECT_DOCUMENT:{previewId}:{lastDoc.OriginalFileName}";
        }

        // Final fallback: labelled ManagedFiles (direct uploads without deep processing)
        var lastFile = await _context.ManagedFiles
            .Where(f => f.Label != null && f.Label != string.Empty)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastFile != null)
            return $"__ACTION:SELECT_DOCUMENT:{lastFile.Id}:{lastFile.FileName}";

        return "No documents found. Please upload a file first.";
    }
}