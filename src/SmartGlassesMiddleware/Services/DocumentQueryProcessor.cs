using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public class DocumentQueryProcessor : IDocumentTaskProcessor
{
    private readonly IRAGService _ragService;
    private readonly IConversationService _conversationService;
    private readonly IGroupConversationMappingService? _groupConversationMappingService;
    private readonly IHistoryService _historyService;
    private readonly IRequestMonitoringService? _monitoringService;
    private readonly ILogger<DocumentQueryProcessor> _logger;

    public DocumentQueryProcessor(
        IRAGService ragService,
        IConversationService conversationService,
        IGroupConversationMappingService? groupConversationMappingService,
        IHistoryService historyService,
        IRequestMonitoringService? monitoringService,
        ILogger<DocumentQueryProcessor> logger)
    {
        _ragService = ragService;
        _conversationService = conversationService;
        _groupConversationMappingService = groupConversationMappingService;
        _historyService = historyService;
        _monitoringService = monitoringService;
        _logger = logger;
    }

    public async Task<object> ProcessBackgroundTaskAsync(BackgroundTaskEntry entry, CancellationToken ct = default)
    {
        var query = entry.PromptText ?? string.Empty;
        try
        {
            // Resolve conversation similar to DocumentsController
            Conversation? conversation = null;
            var groupId = entry.GroupIds?.FirstOrDefault();

            if (entry.ConversationId.HasValue)
            {
                conversation = await _conversationService.GetConversationAsync(entry.ConversationId.Value);
            }

            if (conversation == null && groupId.HasValue && groupId != Guid.Empty && _groupConversation_mapping_check())
            {
                try
                {
                    var mapped = await _groupConversationMappingService!.GetConversationIdForGroupAsync(groupId.Value);
                    if (mapped.HasValue)
                    {
                        conversation = await _conversationService.GetConversationAsync(mapped.Value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to consult group->conversation mapping in background task");
                }
            }

            conversation ??= await _conversationService.DetectOrCreateConversationAsync(query, groupId);

            // Add user message to conversation
            await _conversationService.AddMessageAsync(conversation.Id, "user", query);

            // Perform the document query with conversation context
            var result = await _ragService.QueryDocumentsWithConversationAsync(query, conversation.Id, entry.TopK ?? 5, entry.GroupIds, entry.DocumentIds);

            // Add assistant response to conversation
            await _conversation_service_addassistant(conversation.Id, result.Answer ?? string.Empty);

            // Update group->conversation mapping if applicable
            try
            {
                var mappingGroupId = conversation.ResourceGroupId ?? groupId;
                if (_groupConversation_mapping_check() && mappingGroupId.HasValue && mappingGroupId != Guid.Empty)
                {
                    await _groupConversationMappingService!.UpsertMappingAsync(mappingGroupId.Value, conversation.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert group->conversation mapping in background task");
            }

            // Persist history
            RequestHistory? hist = null;
            try
            {
                var history = new RequestHistory
                {
                    RequestType = "DocumentQuery",
                    InputText = query,
                    Response = result.Answer,
                    DetectedTask = "document_query",
                    CorrelationId = entry.MonitoringRequestId
                };

                hist = await _historyService.SaveRequestAsync(history);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save history for background document query");
            }

            return new { result, requestId = hist?.Id }!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background document query failed");
            throw;
        }
    }

    // Small helpers to avoid long expressions and keep intent clear
    private bool _groupConversation_mapping_check() => _groupConversationMappingService != null;
    private Task _conversation_service_addassistant(Guid conversationId, string content) => _conversationService.AddMessageAsync(conversationId, "assistant", content);
}
