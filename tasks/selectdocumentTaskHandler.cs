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
        // User-uploaded files always have a Label (required by the upload form).
        // Auto-created ManagedFile entries (from DocumentProcessingService background jobs)
        // have an empty Label — exclude those to avoid stale background-processed files
        // from overriding the actual last user upload.
        var lastUserFile = await _context.ManagedFiles
            .Where(f => f.Label != null && f.Label != string.Empty)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastUserFile != null)
            return $"__ACTION:SELECT_DOCUMENT:{lastUserFile.Id}:{lastUserFile.FileName}";

        // Fall back: any ManagedFile regardless of label
        var lastAnyFile = await _context.ManagedFiles
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastAnyFile != null)
            return $"__ACTION:SELECT_DOCUMENT:{lastAnyFile.Id}:{lastAnyFile.FileName}";

        // Final fall back: DocumentMetadatas (deep-processed docs)
        var lastDoc = await _context.DocumentMetadatas
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastDoc == null)
            return "No documents found. Please upload a file first.";

        var previewId = lastDoc.ManagedFileId.HasValue
            ? lastDoc.ManagedFileId.Value.ToString()
            : lastDoc.Id.ToString();
        return $"__ACTION:SELECT_DOCUMENT:{previewId}:{lastDoc.OriginalFileName}";
    }
}