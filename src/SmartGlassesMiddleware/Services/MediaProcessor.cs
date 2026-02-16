using System;
using System.Linq;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class MediaProcessor : IMediaProcessor
{
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IAIService _aiService;
    private readonly IHistoryService _historyService;
    private readonly int _backgroundAiTimeoutSeconds;
    private readonly ILogger<MediaProcessor> _logger;
    private readonly IRagService _mediaRagService;
    private readonly IRequestMonitoringService? _monitoringService;

    public MediaProcessor(IMediaStorageService mediaStorageService, ITranscriptionService transcriptionService, IAIService aiService, IHistoryService historyService, IRagService mediaRagService, IConfiguration configuration, ILogger<MediaProcessor> logger, IRequestMonitoringService? monitoringService = null)
    {
        _mediaStorageService = mediaStorageService;
        _transcriptionService = transcriptionService;
        _aiService = aiService;
        _historyService = historyService;
        _mediaRagService = mediaRagService;
        _backgroundAiTimeoutSeconds = configuration.GetValue<int?>("ServiceTimeouts:MediaBackgroundAiTimeoutSeconds") ?? 30;
        _logger = logger;
        _monitoringService = monitoringService;
    }

    private void ReportProgress(string? monitoringRequestId, int? progress, string? status = null, string? details = null)
    {
        if (string.IsNullOrEmpty(monitoringRequestId)) return;
        try
        {
            _monitoringService?.UpdateRequestStatus(monitoringRequestId, status ?? "InProgress", details, progress);
        }
        catch
        {
            // Swallow to avoid breaking processing if monitoring fails
        }
    }

    public async Task<object> ProcessBackgroundTaskAsync(BackgroundTaskEntry entry, CancellationToken ct = default)
    {
        // Transcribe any audio media items that don't have transcription yet
        var audioExt = new[] { ".wav", ".mp3", ".m4a", ".flac", ".aac" };
        var itemsToTranscribe = entry.MediaItems.Where(i => string.IsNullOrWhiteSpace(i.Transcription) && (audioExt.Contains(Path.GetExtension(i.FileName).ToLowerInvariant()) || (i.ContentType?.StartsWith("audio/") == true))).ToList();
        var totalToTranscribe = itemsToTranscribe.Count;
        var transcribedCount = 0;
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
                    transcribedCount++;
                    if (totalToTranscribe > 0)
                    {
                        var percent = 10 + (int)Math.Round((transcribedCount / (double)totalToTranscribe) * 20);
                        ReportProgress(entry.MonitoringRequestId, percent, "Transcribing", $"Transcribed {transcribedCount}/{totalToTranscribe}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to transcribe background media {Key}", item.StorageKey);
                }
            }
        }

        // Mark transcription phase done
        ReportProgress(entry.MonitoringRequestId, 30, "Transcription", "Transcription phase complete");

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

        // Retrieval-first: ask the media RAG for context quickly and include it only when relevant
        try
        {
            ReportProgress(entry.MonitoringRequestId, 40, "Retrieval", "Retrieving RAG context");
            var retrievalQuery = !string.IsNullOrWhiteSpace(promptText)
                ? promptText
                : entry.MediaItems.Select(i => i.Transcription).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? textToProcess;

            var ragTask = _mediaRagService.GetRagContextAsync(retrievalQuery, maxResults: 3, monitoringRequestId: entry.MonitoringRequestId);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20));
            var completedTask = await Task.WhenAny(ragTask, timeoutTask);
            if (completedTask == ragTask)
            {
                var ragContext = await ragTask;
                if (!string.IsNullOrWhiteSpace(ragContext))
                {
                    _logger.LogInformation("Including media RAG context in AI prompt (len={Len})", ragContext.Length);
                    textToProcess = promptText + "\n\nContext:\n" + ragContext + "\n\n" + string.Join("\n\n", partsForAi);
                    ReportProgress(entry.MonitoringRequestId, 55, "Retrieval", "RAG context retrieved");
                }
                else
                {
                    _logger.LogInformation("No media RAG context found for query, proceeding without it");
                    ReportProgress(entry.MonitoringRequestId, 45, "Retrieval", "No relevant RAG context found");
                }
            }
            else
            {
                _logger.LogWarning("RAG context retrieval timed out; proceeding without context");
                ReportProgress(entry.MonitoringRequestId, 45, "Retrieval", "RAG lookup timed out");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media RAG lookup failed; proceeding without context");
            ReportProgress(entry.MonitoringRequestId, 45, "Retrieval", "RAG lookup failed");
        }

        ReportProgress(entry.MonitoringRequestId, 60, "AI", "Sending to AI service");
        // Use a configurable timeout to prevent hanging on the AI service
        string task = "", response = "";
        bool usedAdvancedModel = false;
        try
        {
            using var aiCts = new CancellationTokenSource(TimeSpan.FromSeconds(_backgroundAiTimeoutSeconds));
            (task, response, usedAdvancedModel) = await _aiService.ProcessRequestAsync(textToProcess, aiCts.Token);
            ReportProgress(entry.MonitoringRequestId, 95, "AI", "AI response received");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AI service call timed out after {Seconds} seconds for background task {Id}; proceeding with fallback response", _backgroundAiTimeoutSeconds, entry.Id);
            ReportProgress(entry.MonitoringRequestId, 65, "AI", "AI service timed out; using fallback");
            task = "document_analysis";
            response = "The AI service did not respond within the time limit. The request was received and will be processed asynchronously.";
            usedAdvancedModel = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service processing failed for background task {Id}; proceeding with fallback", entry.Id);
            ReportProgress(entry.MonitoringRequestId, 65, "AI", "AI service unavailable; using fallback");
            task = "document_analysis";
            response = "The AI service encountered an error. The request was received and may be retried.";
            usedAdvancedModel = false;
        }

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
            // If this background task has an associated monitoring request id, attach it so UI can group
            if (!string.IsNullOrWhiteSpace(entry.MonitoringRequestId)) history.CorrelationId = entry.MonitoringRequestId;
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
