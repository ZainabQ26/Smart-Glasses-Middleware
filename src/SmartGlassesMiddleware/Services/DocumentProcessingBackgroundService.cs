using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Background service that processes unprocessed documents on startup and periodically
/// </summary>
public class DocumentProcessingBackgroundService : BackgroundService
{
    private readonly ILogger<DocumentProcessingBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const int CheckIntervalMinutes = 15;

    public DocumentProcessingBackgroundService(
        ILogger<DocumentProcessingBackgroundService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document Processing Background Service starting");

        // Wait a bit for the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Process on startup
        await ProcessUnprocessedDocumentsAsync();

        // Then check periodically
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(CheckIntervalMinutes));
        
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessUnprocessedDocumentsAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Document Processing Background Service is stopping");
        }
    }

    private async Task ProcessUnprocessedDocumentsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var ragService = scope.ServiceProvider.GetRequiredService<IRAGService>();

            var unprocessedDocs = await ragService.GetUnprocessedDocumentsAsync();
            
            if (unprocessedDocs.Any())
            {
                _logger.LogInformation("Found {Count} unprocessed documents. Starting reprocessing...", unprocessedDocs.Count);
                var reprocessedCount = await ragService.ReprocessFailedDocumentsAsync();
                _logger.LogInformation("Reprocessed {Count} documents successfully", reprocessedCount);
            }
            else
            {
                _logger.LogDebug("No unprocessed documents found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing unprocessed documents");
        }
    }
}
