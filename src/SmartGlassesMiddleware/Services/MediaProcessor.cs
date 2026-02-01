using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class MediaProcessor : IMediaProcessor
{
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;
    private readonly ILogger<MediaProcessor> _logger;

    public MediaProcessor(IMediaStorageService mediaStorageService, ITranscriptionService transcriptionService, IAIService aiService, IHistoryService historyService, ILogger<MediaProcessor> logger)
    {
        _mediaStorageService = mediaStorageService;
        _transcriptionService = transcriptionService;
        _aiService = aiService;
        _historyService = historyService;
        _logger = logger;
    }

    public async Task<object> ProcessBackgroundTaskAsync(BackgroundTaskEntry entry, CancellationToken ct = default)
    {
        // Transcribe any audio media items that don't have transcription yet
        var audioExt = new[] { ".wav", ".mp3", ".m4a", ".flac", ".aac" };
        foreach (var item in entry.MediaItems)
        {
            if (!string.IsNullOrWhiteSpace(item.Transcription)) continue;
            var ext = Path.GetExtension(item.FileName).ToLowerInvariant();
            if (audioExt.Contains(ext) || (item.ContentType?.StartsWith("audio/") == true))
            {
                try
                {
                    using var stream = await _mediaStorageService.DownloadMediaAsync(item.StorageKey);
                    var contentType = item.ContentType ?? string.Empty;
                    item.Transcription = await _transcriptionService.TranscribeAsync(stream, item.FileName, contentType);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to transcribe background media {Key}", item.StorageKey);
                }
            }
        }

        // Build parts for AI
        var partsForAi = new List<string>();
        foreach (var item in entry.MediaItems)
        {
            var partText = $"MediaFileName: {item.FileName}\nMediaStorageKey: {item.StorageKey}\nMediaContentType: {item.ContentType}";
            if (!string.IsNullOrWhiteSpace(item.Transcription)) partText += $"\nTranscription: {item.Transcription}";
            partsForAi.Add(partText);
        }

        var promptText = entry.PromptText ?? string.Empty;
        var textToProcess = promptText + "\n\n" + string.Join("\n\n", partsForAi);

        var (task, response, usedAdvancedModel) = await _aiService.ProcessRequestAsync(textToProcess);

        // Save history
        try
        {
            var history = new RequestHistory
            {
                RequestType = "Media",
                InputText = textToProcess,
                DetectedTask = task,
                Response = response,
                UsedAdvancedModel = usedAdvancedModel,
                MediaStorageKey = string.Join(",", entry.MediaItems.Select(i => i.StorageKey))
            };
            history = await _historyService.SaveRequestAsync(history);

            var items = entry.MediaItems.Select(i => new { fileName = i.FileName, contentType = i.ContentType, storageKey = i.StorageKey, transcription = i.Transcription }).ToList();
            return new { task, response, usedAdvancedModel, requestId = history.Id, items };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save history for background task {Id}", entry.Id);
            var items = entry.MediaItems.Select(i => new { fileName = i.FileName, contentType = i.ContentType, storageKey = i.StorageKey, transcription = i.Transcription }).ToList();
            return new { task, response, usedAdvancedModel, items };
        }
    }
}
