using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System.Linq;

public class SelectDocumentTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;

    public string Name => "select-document";

    public IReadOnlyList<string> Keywords => new[]
    {
        "open document",
        "abrir documento",
        "open last document",
        "abrir último documento",
        "open the document",
        "abrir el documento",
        "open pdf",
        "abrir pdf",
        "open the pdf",
        "abrir el pdf",
        "load document",
        "cargar documento",
        "view document",
        "ver documento"
    };

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
            .Where(d => !string.IsNullOrWhiteSpace(d.StorageKey)
                && !EF.Functions.Like(d.StorageKey, "FAILED:%")
                && !EF.Functions.Like(d.StorageKey, "PENDING:%")
                && d.FileSize > 0)
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastDoc != null)
        {
            // Prefer ManagedFileId so the preview endpoint hits ManagedFiles first,
            // but fall back to the DocumentMetadata ID (preview endpoint now handles both).
            var previewId = lastDoc.ManagedFileId.HasValue
                ? lastDoc.ManagedFileId.Value.ToString()
                : lastDoc.Id.ToString();
            var displayName = string.IsNullOrWhiteSpace(lastDoc.OriginalFileName)
                ? lastDoc.FileName
                : lastDoc.OriginalFileName;
            return $"__ACTION:SELECT_DOCUMENT:{previewId}:{displayName}";
        }

        // Final fallback: labelled ManagedFiles (direct uploads without deep processing)
        var lastFile = await _context.ManagedFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.StorageKey)
                && !EF.Functions.Like(f.StorageKey, "FAILED:%")
                && !EF.Functions.Like(f.StorageKey, "PENDING:%")
                && f.FileSize > 0)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastFile != null)
        {
            var displayName = string.IsNullOrWhiteSpace(lastFile.Label)
                ? lastFile.FileName
                : lastFile.Label;
            return $"__ACTION:SELECT_DOCUMENT:{lastFile.Id}:{displayName}";
        }

        return "No documents found. Please upload a file first.";
    }
}