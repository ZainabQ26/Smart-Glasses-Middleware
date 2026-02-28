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
        var lastFile = await _context.ManagedFiles
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (lastFile == null)
            return "No documents found. Please upload a file first.";

        // Return a special action token the frontend intercepts to open the file preview
        return $"__ACTION:SELECT_DOCUMENT:{lastFile.Id}:{lastFile.FileName}";
    }
}