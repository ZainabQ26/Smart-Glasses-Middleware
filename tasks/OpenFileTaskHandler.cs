using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class OpenFileTaskHandler : ITaskHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OpenFileTaskHandler> _logger;

    public string Name => "open-file";

    public OpenFileTaskHandler(IServiceProvider serviceProvider, ILogger<OpenFileTaskHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        _logger.LogInformation("Handling open-file request.");

        // Lazily resolve DbContext to prevent startup crashes if DB is not ready
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _logger.LogInformation("Fetching the 5 most recent documents.");
        var recentFiles = await dbContext.ManagedFiles
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
