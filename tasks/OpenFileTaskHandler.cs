using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using Microsoft.EntityFrameworkCore;

public class OpenFileTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;

    public string Name => "open-file";

    public IReadOnlyList<string> Keywords => new[]
    {
        "open file",
        "abrir archivo",
        "show files",
        "mostrar archivos",
        "recent files",
        "archivos recientes",
        "list files",
        "listar archivos",
        "open a file",
        "abrir un archivo",
        "show recent files",
        "mostrar archivos recientes",
        "select document",
        "seleccionar documento"
    };

    public OpenFileTaskHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> HandleAsync(string input)
    {
        var recentFiles = await _context.ManagedFiles
            .AsNoTracking()
            .Where(f => !string.IsNullOrWhiteSpace(f.StorageKey)
                && !EF.Functions.Like(f.StorageKey, "FAILED:%")
                && !EF.Functions.Like(f.StorageKey, "PENDING:%")
                && f.FileSize > 0)
            .OrderByDescending(f => f.UploadedAt)
            .Take(5)
            .Select(f => new
            {
                f.Id,
                DisplayName = string.IsNullOrWhiteSpace(f.Label) ? f.FileName : f.Label
            })
            .ToListAsync();

        if (recentFiles.Count == 0)
            return "No uploaded files found. Please upload a file first.";

        return "__ACTION:SHOW_RECENT_FILES__";
    }
}
