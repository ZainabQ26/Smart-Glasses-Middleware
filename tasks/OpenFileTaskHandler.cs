using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class OpenFileTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OpenFileTaskHandler> _logger;

    public string Name => "open-file";

    public OpenFileTaskHandler(ApplicationDbContext context, ILogger<OpenFileTaskHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        _logger.LogInformation("Fetching the 5 most recent documents.");
        var recentFiles = await _context.ManagedFiles
            .OrderByDescending(f => f.UploadedAt)
            .Take(5)
            .Select(f => f.FileName)
            .ToListAsync();

        if (recentFiles.Any())
        {
            return "Here are the 5 most recent documents:\n" + string.Join("\n", recentFiles.Select(f => $"• {f}"));
        }
        else
        {
            return "No documents have been uploaded yet.";
        }
    }
}
