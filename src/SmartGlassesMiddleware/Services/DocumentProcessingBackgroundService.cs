using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Background service that processes unprocessed documents on startup and periodically
/// </summary>
public class DocumentProcessingBackgroundService : BackgroundService
{
    private readonly ILogger<DocumentProcessingBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _checkIntervalMinutes;
    private readonly bool _ragEnabled;

    public DocumentProcessingBackgroundService(
        ILogger<DocumentProcessingBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _checkIntervalMinutes = configuration.GetValue<int?>("RAG:BackgroundProcessingIntervalMinutes") ?? 15;
        _ragEnabled = configuration.GetValue<bool?>("RAG:Enabled") ?? true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_ragEnabled)
        {
            _logger.LogInformation("Document Processing Background Service is disabled because RAG:Enabled=false");
            return;
        }

        _logger.LogInformation("Document Processing Background Service starting (check interval: {Minutes} minutes)", _checkIntervalMinutes);

        // Wait a bit for the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Process on startup
        await ProcessUnprocessedDocumentsAsync();

        // Then check periodically
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_checkIntervalMinutes));

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
