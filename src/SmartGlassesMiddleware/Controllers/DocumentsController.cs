using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Hubs;
using Microsoft.Extensions.DependencyInjection;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly ILogger<DocumentsController> _logger;
    private readonly IDocumentProcessingService _processingService;
    private readonly IRAGService _ragService;
    private readonly IDocumentHighlightService _highlightService;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IRequestMonitoringService _monitoringService;
    private readonly IHistoryService _historyService;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly IConversationService _conversationService;
    private readonly IGroupConversationMappingService? _groupConversationMappingService;
    private readonly ICacheService _cacheService;
    private readonly IHubContext<ResourceHub>? _resourceHub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _ragEnabled;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private static readonly TimeSpan QueryMonitoringTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QueryDocumentLookupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan QueryExecutionTimeout = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan QueryHistorySaveTimeout = TimeSpan.FromSeconds(3);

    public DocumentsController(
        ILogger<DocumentsController> logger,
        IDocumentProcessingService processingService,
        IRAGService ragService,
        IDocumentHighlightService highlightService,
        ApplicationDbContext context,
        IConfiguration configuration,
        IRequestMonitoringService monitoringService,
        IHistoryService historyService,
        IMediaStorageService mediaStorageService,
        IConversationService conversationService,
        IServiceScopeFactory scopeFactory,
        IGroupConversationMappingService? groupConversationMappingService = null,
        ICacheService? cacheService = null,
        IHubContext<ResourceHub>? resourceHub = null)
    {
        _logger = logger;
        _processingService = processingService;
        _ragService = ragService;
        _highlightService = highlightService;
        _context = context;
        _configuration = configuration;
        _monitoringService = monitoringService;
        _historyService = historyService;
        _mediaStorageService = mediaStorageService;
        _conversationService = conversationService;
        _scopeFactory = scopeFactory;
        _ragEnabled = configuration.GetValue<bool?>("RAG:Enabled") ?? true;
        _groupConversationMappingService = groupConversationMappingService;
        _cacheService = cacheService ?? new InMemoryCacheService(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<InMemoryCacheService>());
        _resourceHub = resourceHub;
    }

    /// <summary>
    /// Upload a new document for processing and indexing
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadDocument(IFormFile? file, [FromQuery] List<Guid>? groupIds = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is required and cannot be empty");

        var maxFileSizeMb = int.TryParse(_configuration["RAG:MaxFileSizeMB"], out var size) ? size : 50;
        if (file.Length > maxFileSizeMb * 1024 * 1024)
            return BadRequest($"File size exceeds maximum of {maxFileSizeMb}MB");

        var requestId = await _monitoringService.StartRequest("DocumentUpload", file.FileName ?? "unknown");
        var originalFileName = file.FileName ?? string.Empty;
        var originalContentType = file.ContentType ?? "application/octet-stream";

        try
        {
            var documentId = Guid.NewGuid();
            var placeholderBaseName = Path.GetFileNameWithoutExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(placeholderBaseName))
            {
                placeholderBaseName = "document";
            }
            var placeholderPdfName = $"{placeholderBaseName}.pdf";

            // Read uploaded bytes once and process in a background task.
            // This keeps the request short and avoids CloudFront 504 timeouts.
            byte[] uploadedBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                uploadedBytes = ms.ToArray();
            }

            _logger.LogInformation("Upload received and queued: {FileName}, {Size} bytes", originalFileName, uploadedBytes.LongLength);

            // Create placeholder records immediately so the upload returns successfully.
            // StorageKey/FileSize are populated when background conversion+upload completes.
            var metadata = new DocumentMetadata
            {
                Id = documentId,
                FileName = placeholderPdfName,
                OriginalFileName = originalFileName,
                ContentType = "application/pdf",
                StorageKey = string.Empty,
                UploadedAt = DateTime.UtcNow,
                FileSize = 0,
                DocumentType = "pdf",
                IsProcessed = false
            };
            _context.DocumentMetadatas.Add(metadata);

            // Also create a ManagedFile so the upload appears in the admin files panel
            var managedFile = new ManagedFile
            {
                Id = documentId,
                FileName = placeholderPdfName,
                Label = originalFileName,
                ContentType = "application/pdf",
                StorageKey = string.Empty,
                UploadedAt = DateTime.UtcNow,
                FileSize = 0
            };
            _context.ManagedFiles.Add(managedFile);
            metadata.ManagedFileId = managedFile.Id;

            await _context.SaveChangesAsync();

            // Write group associations
            if (groupIds != null && groupIds.Any())
            {
                foreach (var groupId in groupIds)
                {
                    var group = await _context.ResourceGroups.FindAsync(groupId);
                    if (group != null)
                    {
                        metadata.DocumentGroups.Add(new DocumentGroup
                        {
                            DocumentMetadataId = metadata.Id,
                            ResourceGroupId = groupId
                        });
                    }
                }
                await _context.SaveChangesAsync();
                try { await InvalidateGroupsCache(); await NotifyGroupsChangedAsync(); } catch { }
            }

            try { await InvalidateFilesCache(); await NotifyFilesChangedAsync(); } catch { }

            await _monitoringService.UpdateRequestStatus(requestId, "Queued", "Upload accepted. Converting and storing document in the background...");

            _ = Task.Run(() => ProcessQueuedDocumentUploadAsync(documentId, uploadedBytes, originalFileName, originalContentType, requestId));

            return Accepted(new { id = documentId, requestId, status = "processing" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document {FileName}", originalFileName);
            await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new { error = "Failed to upload document", details = ex.Message });
        }
    }

    private async Task ProcessQueuedDocumentUploadAsync(
        Guid documentId,
        byte[] uploadedBytes,
        string originalFileName,
        string originalContentType,
        string requestId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processingService = scope.ServiceProvider.GetRequiredService<IDocumentProcessingService>();
            var ragService = scope.ServiceProvider.GetRequiredService<IRAGService>();
            var mediaStorageService = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var monitoringService = scope.ServiceProvider.GetService<IRequestMonitoringService>();

            if (monitoringService != null)
            {
                await monitoringService.UpdateRequestStatus(requestId, "Processing", "Converting uploaded document to PDF...");
            }

            using var input = new MemoryStream(uploadedBytes);
            var (pdfStream, pdfFileName) = await processingService.ConvertToPdfAsync(input, originalFileName, originalContentType, requestId);
            pdfStream.Position = 0;

            if (pdfStream.Length == 0)
            {
                throw new InvalidOperationException("PDF conversion produced empty stream");
            }

            var uploadKey = $"uploads/{documentId}.pdf";
            await mediaStorageService.UploadFileAsync(uploadKey, pdfStream, "application/pdf");

            var metadata = await db.DocumentMetadatas.FirstOrDefaultAsync(d => d.Id == documentId);
            var managedFile = await db.ManagedFiles.FirstOrDefaultAsync(f => f.Id == documentId);

            if (metadata != null)
            {
                metadata.FileName = pdfFileName;
                metadata.OriginalFileName = originalFileName;
                metadata.ContentType = "application/pdf";
                metadata.StorageKey = uploadKey;
                metadata.FileSize = pdfStream.Length;
                metadata.DocumentType = "pdf";
                metadata.IsProcessed = !_ragEnabled;
                metadata.ProcessedAt = _ragEnabled ? null : DateTime.UtcNow;
                metadata.ProcessingError = _ragEnabled ? null : "RAG disabled: indexing skipped.";
                metadata.ManagedFileId = documentId;
            }

            if (managedFile != null)
            {
                managedFile.FileName = pdfFileName;
                managedFile.Label = originalFileName;
                managedFile.ContentType = "application/pdf";
                managedFile.StorageKey = uploadKey;
                managedFile.FileSize = pdfStream.Length;
            }

            await db.SaveChangesAsync();

            try
            {
                await InvalidateFilesCache(db);
                await NotifyFilesChangedAsync();
            }
            catch (Exception notifyEx)
            {
                _logger.LogWarning(notifyEx, "Failed to notify file list refresh after queued upload {DocumentId}", documentId);
            }

            if (_ragEnabled && monitoringService != null)
            {
                await monitoringService.UpdateRequestStatus(requestId, "Processing", "Extracting text and generating embeddings...", 75);
            }

            if (_ragEnabled)
            {
                await processingService.ReprocessDocumentAsync(documentId);
                await ragService.GenerateEmbeddingsAsync(documentId);
            }

            if (monitoringService != null)
            {
                await monitoringService.CompleteRequest(
                    requestId,
                    true,
                    _ragEnabled
                        ? $"Document {pdfFileName} uploaded and indexed"
                        : $"Document {pdfFileName} uploaded. RAG indexing skipped.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queued upload failed for document {DocumentId}", documentId);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var monitoringService = scope.ServiceProvider.GetService<IRequestMonitoringService>();

                var metadata = await db.DocumentMetadatas.FirstOrDefaultAsync(d => d.Id == documentId);
                if (metadata != null)
                {
                    metadata.ProcessingError = ex.Message;
                    await db.SaveChangesAsync();
                }

                if (monitoringService != null)
                {
                    await monitoringService.CompleteRequest(requestId, false, error: ex.Message);
                }
            }
            catch (Exception finalizeEx)
            {
                _logger.LogWarning(finalizeEx, "Failed to finalize queued upload failure state for {DocumentId}", documentId);
            }
        }
    }

    private Task NotifyFilesChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("filesChanged")
            : Task.CompletedTask;
    }

    private async Task InvalidateFilesCache(ApplicationDbContext? ctx = null)
    {
        try
        {
            var db = ctx ?? _context;
            var totalCount = await db.ManagedFiles.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)DefaultPageSize);

            var tasks = new List<Task>();
            for (int page = 1; page <= totalPages + 1; page++)
            {
                tasks.Add(_cacheService.RemoveAsync($"admin:files:page:{page}:size:{DefaultPageSize}"));
                tasks.Add(_cacheService.RemoveAsync($"admin:files:page:{page}:size:{MaxPageSize}"));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating files cache");
        }
    }

    /// <summary>
    /// Query documents using RAG (Retrieval-Augmented Generation)
    /// </summary>
    [HttpPost("query")]
    [ProducesResponseType(typeof(QueryResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueryDocuments([FromBody] QueryRequest request)
    {
        _logger.LogInformation("=== DOCUMENT QUERY REQUEST RECEIVED ===");
        _logger.LogInformation("Query: {Query}", request.Query);
        _logger.LogInformation("TopK: {TopK}, GroupIds: {GroupIds}, DocumentIds: {DocumentIds}", request.TopK, request.GroupIds?.Count ?? 0, request.DocumentIds?.Count ?? 0);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            _logger.LogWarning("Query is empty, returning BadRequest");
            return BadRequest("Query cannot be empty");
        }

        string? requestId = null;
        var groupId = request.GroupIds?.FirstOrDefault();
        Conversation? conversation = null;

        try
        {
            var clientRequestId = !string.IsNullOrWhiteSpace(request.RequestId) ? request.RequestId : null;
            requestId = await StartMonitoringRequestSafelyAsync(
                "DocumentQuery",
                $"Query: {request.Query.Substring(0, Math.Min(50, request.Query.Length))}...",
                clientRequestId);

            await TryUpdateMonitoringStatusAsync(requestId, "Searching documents...");

            var enableConversationTracking = _configuration.GetValue<bool?>("RAG:EnableConversationTrackingInQuery") ?? false;
            if (enableConversationTracking)
            {
                try
                {
                    if (request.ConversationId.HasValue)
                    {
                        conversation = await _conversationService.GetConversationAsync(request.ConversationId.Value);
                        if (conversation == null)
                        {
                            _logger.LogWarning("Conversation {ConversationId} not found, falling back to detection", request.ConversationId);
                        }
                    }

                    if (conversation == null && groupId.HasValue && groupId != Guid.Empty && _groupConversationMappingService != null)
                    {
                        var mapped = await _groupConversationMappingService.GetConversationIdForGroupAsync(groupId.Value);
                        if (mapped.HasValue)
                        {
                            conversation = await _conversationService.GetConversationAsync(mapped.Value);
                            if (conversation == null)
                            {
                                _logger.LogWarning("Mapped conversation {ConversationId} for group {GroupId} not found", mapped, groupId);
                            }
                        }
                    }

                    conversation ??= await _conversationService.DetectOrCreateConversationAsync(request.Query, groupId);
                    _logger.LogInformation("Using conversation {ConversationId} for query", conversation.Id);

                    try
                    {
                        var (topicChanged, newConversation) = await _conversationService.DetectTopicChangeAsync(conversation.Id, request.Query);
                        if (topicChanged && newConversation != null)
                        {
                            _logger.LogInformation("Auto-switching conversation due to topic change: {NewId}", newConversation.Id);
                            conversation = newConversation;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Topic change detection threw an exception for conversation {ConversationId}", conversation.Id);
                    }

                    await _conversationService.AddMessageAsync(conversation.Id, "user", request.Query, null, request.DocumentIds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Conversation tracking unavailable (possibly pending DB migrations). Proceeding with stateless RAG query.");
                    conversation = null;
                }
            }
            else
            {
                _logger.LogInformation("Conversation tracking disabled for query endpoint. Running stateless document query.");
            }

            List<Guid>? effectiveDocumentIds = request.DocumentIds;
            if (request.DocumentIds != null && request.DocumentIds.Any())
            {
                List<(Guid Id, string DisplayName, bool IsProcessed, string? ProcessingError)> requestedDocuments;
                try
                {
                    using var lookupCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                    lookupCts.CancelAfter(QueryDocumentLookupTimeout);
                    requestedDocuments = await _context.DocumentMetadatas
                        .Where(d => request.DocumentIds.Contains(d.Id))
                        .Select(d => new
                        {
                            d.Id,
                            DisplayName = string.IsNullOrWhiteSpace(d.OriginalFileName) ? d.FileName : d.OriginalFileName,
                            d.IsProcessed,
                            d.ProcessingError
                        })
                        .Select(d => new ValueTuple<Guid, string, bool, string?>(d.Id, d.DisplayName, d.IsProcessed, d.ProcessingError))
                        .ToListAsync(lookupCts.Token);
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Timed out loading requested document metadata; continuing without readiness gating.");
                    requestedDocuments = new List<(Guid Id, string DisplayName, bool IsProcessed, string? ProcessingError)>();
                }

                var failedDocuments = requestedDocuments
                    .Where(d => !string.IsNullOrWhiteSpace(d.ProcessingError))
                    .ToList();

                if (failedDocuments.Any())
                {
                    var failureResult = new QueryResult
                    {
                        Answer = failedDocuments.Count == 1
                            ? $"{failedDocuments[0].DisplayName} could not be processed yet. {failedDocuments[0].ProcessingError}"
                            : "One or more selected documents could not be processed yet. Re-upload them or try again later.",
                        ConversationId = conversation?.Id,
                        Sources = new List<DocumentSource>(),
                        ReferencedSources = new List<DocumentSource>(),
                        ConversationSources = new List<ConversationSource>(),
                        UsedAdvancedModel = false,
                        ModelUsed = "document-processing-error",
                        TokenCount = 0
                    };

                    if (conversation != null)
                    {
                        try
                        {
                            await _conversationService.AddMessageAsync(conversation.Id, "assistant", failureResult.Answer);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to save processing-error assistant message to conversation {ConversationId}", conversation.Id);
                        }
                    }

                    await TryCompleteMonitoringRequestAsync(requestId, true, result: "Document processing error returned to client");
                    return Ok(failureResult);
                }

                var pendingDocuments = requestedDocuments
                    .Where(d => !d.IsProcessed)
                    .ToList();

                if (pendingDocuments.Any())
                {
                    _logger.LogInformation("Requested documents are still processing: {Ids}",
                        string.Join(", ", pendingDocuments.Select(d => d.Id)));
                    await TryUpdateMonitoringStatusAsync(requestId, "Processing",
                        $"Waiting for {pendingDocuments.Count} document(s) to finish indexing", 35);

                    var pendingResult = new QueryResult
                    {
                        Answer = pendingDocuments.Count == 1
                            ? $"{pendingDocuments[0].DisplayName} is still being processed. Try again in a few seconds."
                            : $"{pendingDocuments.Count} selected documents are still being processed. Try again in a few seconds.",
                        ConversationId = conversation?.Id,
                        Sources = new List<DocumentSource>(),
                        ReferencedSources = new List<DocumentSource>(),
                        ConversationSources = new List<ConversationSource>(),
                        UsedAdvancedModel = false,
                        ModelUsed = "document-processing-pending",
                        TokenCount = 0
                    };

                    if (conversation != null)
                    {
                        try
                        {
                            await _conversationService.AddMessageAsync(conversation.Id, "assistant", pendingResult.Answer);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to save pending assistant message to conversation {ConversationId}", conversation.Id);
                        }
                    }

                    await TryCompleteMonitoringRequestAsync(requestId, true, result: "Documents still processing");
                    return Ok(pendingResult);
                }

                if (requestedDocuments.Any())
                {
                    effectiveDocumentIds = requestedDocuments.Select(d => d.Id).ToList();
                }
            }

            QueryResult result;
            if (conversation != null)
            {
                _logger.LogInformation("Calling RAG service QueryDocumentsWithConversationAsync...");
                result = await _ragService.QueryDocumentsWithConversationAsync(
                    request.Query,
                    conversation.Id,
                    request.TopK ?? 5,
                    request.GroupIds,
                    effectiveDocumentIds).WaitAsync(QueryExecutionTimeout);
            }
            else
            {
                _logger.LogInformation("Falling back to stateless QueryDocumentsAsync (no conversation context)...");
                result = await _ragService.QueryDocumentsAsync(
                    request.Query,
                    request.TopK ?? 5,
                    request.GroupIds,
                    effectiveDocumentIds,
                    monitoringRequestId: requestId).WaitAsync(QueryExecutionTimeout);
            }

            _logger.LogInformation("RAG service returned result. Answer length: {Length}", result.Answer?.Length ?? 0);

            if (conversation != null)
            {
                try
                {
                    await _conversationService.AddMessageAsync(conversation.Id, "assistant", result.Answer ?? string.Empty);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save assistant message to conversation {ConversationId}", conversation.Id);
                }

                try
                {
                    var mappingGroupId = conversation.ResourceGroupId ?? groupId;
                    if (_groupConversationMappingService != null && mappingGroupId.HasValue && mappingGroupId != Guid.Empty)
                    {
                        await _groupConversationMappingService.UpsertMappingAsync(mappingGroupId.Value, conversation.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upsert group->conversation mapping");
                }
            }

            await TryCompleteMonitoringRequestAsync(requestId, true,
                result: $"Found {result.Sources?.Count ?? 0} sources");
            _logger.LogInformation("=== DOCUMENT QUERY COMPLETED SUCCESSFULLY ===");

            QueueDocumentQueryHistorySave(request.Query, result.Sources?.Count ?? 0, requestId);

            result.ConversationId = conversation?.Id;
            return Ok(result);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Query timed out for query: {Query}", request.Query);
            if (requestId != null)
                await TryCompleteMonitoringRequestAsync(requestId, false, error: "Request timed out");
            return StatusCode(504, new { error = "Request timed out", details = ex.Message });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Query was cancelled (client disconnected) for query: {Query}", request.Query);
            if (requestId != null)
                await TryCompleteMonitoringRequestAsync(requestId, false, error: "Request cancelled by client");
            return StatusCode(499, new { error = "Request cancelled", details = "Client disconnected" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== ERROR QUERYING DOCUMENTS === Message: {Message}, StackTrace: {StackTrace}",
                ex.Message, ex.StackTrace);
            if (requestId != null)
                await TryCompleteMonitoringRequestAsync(requestId, false, error: ex.Message);
            return StatusCode(500, new { error = "Failed to query documents", details = ex.Message });
        }
    }

    private async Task<string> StartMonitoringRequestSafelyAsync(string requestType, string description, string? requestId = null)
    {
        var fallbackId = !string.IsNullOrWhiteSpace(requestId) ? requestId! : Guid.NewGuid().ToString();
        try
        {
            return await _monitoringService
                .StartRequest(requestType, description, requestId)
                .WaitAsync(QueryMonitoringTimeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("StartRequest timed out for {RequestType}; continuing without blocking", requestType);
            return fallbackId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StartRequest failed for {RequestType}; continuing without blocking", requestType);
            return fallbackId;
        }
    }

    private async Task TryUpdateMonitoringStatusAsync(string? requestId, string status, string? details = null, int? progress = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        try
        {
            await _monitoringService
                .UpdateRequestStatus(requestId, status, details, progress)
                .WaitAsync(QueryMonitoringTimeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("UpdateRequestStatus timed out for request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UpdateRequestStatus failed for request {RequestId}", requestId);
        }
    }

    private async Task TryCompleteMonitoringRequestAsync(string? requestId, bool success, string? result = null, string? error = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        try
        {
            await _monitoringService
                .CompleteRequest(requestId, success, result, error)
                .WaitAsync(QueryMonitoringTimeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("CompleteRequest timed out for request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompleteRequest failed for request {RequestId}", requestId);
        }
    }

    private void QueueDocumentQueryHistorySave(string query, int sourceCount, string? requestId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var historyService = scope.ServiceProvider.GetRequiredService<IHistoryService>();
                var hist = new RequestHistory
                {
                    RequestType = "DocumentQuery",
                    InputText = query,
                    Response = $"Found {sourceCount} sources",
                    DetectedTask = "document_query",
                    CorrelationId = requestId
                };

                await historyService.SaveRequestAsync(hist).WaitAsync(QueryHistorySaveTimeout);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timed out while saving document query history");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save document query history");
            }
        });
    }

    /// <summary>
    /// Get a highlighted copy of a document showing where specific information is located
    /// </summary>
    [HttpPost("{documentId}/highlight")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HighlightDocument(Guid documentId, [FromBody] HighlightRequest request)
    {
        try
        {
            Stream highlightedStream;

            if (request.ChunkIds != null && request.ChunkIds.Any())
            {
                highlightedStream = await _highlightService.HighlightDocumentAsync(documentId, request.ChunkIds);
            }
            else if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                highlightedStream = await _highlightService.HighlightTextLocationsAsync(documentId, request.SearchText);
            }
            else
            {
                return BadRequest("Either ChunkIds or SearchText must be provided");
            }

            var document = await _context.DocumentMetadatas.FindAsync(documentId);
            var fileName = $"highlighted_{document?.FileName ?? "document"}";

            return File(highlightedStream, document?.ContentType ?? "application/pdf", fileName);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error highlighting document");
            return StatusCode(500, new { error = "Failed to highlight document", details = ex.Message });
        }
    }

    /// <summary>
    /// List all uploaded documents
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DocumentMetadata>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var documents = await _context.DocumentMetadatas
            .Where(d => !string.IsNullOrWhiteSpace(d.StorageKey) && d.FileSize > 0)
            .OrderByDescending(d => d.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                OriginalFileName = string.IsNullOrWhiteSpace(d.OriginalFileName) ? d.FileName : d.OriginalFileName,
                d.DocumentType,
                d.TotalPages,
                d.FileSize,
                d.UploadedAt,
                d.IsProcessed,
                d.ProcessedAt,
                d.ManagedFileId,
                ChunkCount = d.Chunks.Count
            })
            .ToListAsync();

        var totalCount = await _context.DocumentMetadatas
            .Where(d => !string.IsNullOrWhiteSpace(d.StorageKey) && d.FileSize > 0)
            .CountAsync();

        return Ok(new
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Documents = documents
        });
    }

    /// <summary>
    /// Get details of a specific document
    /// </summary>
    [HttpGet("{documentId}")]
    [ProducesResponseType(typeof(DocumentMetadata), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(Guid documentId)
    {
        var document = await _context.DocumentMetadatas
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return NotFound();

        return Ok(new
        {
            document.Id,
            document.FileName,
            document.ContentType,
            document.DocumentType,
            document.TotalPages,
            document.FileSize,
            document.UploadedAt,
            document.IsProcessed,
            ManagedFileId = document.ManagedFileId,
            document.ProcessedAt,
            Chunks = document.Chunks.Select(c => new
            {
                c.Id,
                c.PageNumber,
                c.ChunkIndex,
                Preview = c.ChunkText.Length > 100 ? c.ChunkText.Substring(0, 100) + "..." : c.ChunkText
            }).ToList()
        });
    }

    /// <summary>
    /// Download or preview a document file
    /// </summary>
    [HttpGet("{documentId}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(Guid documentId)
    {
        var document = await _context.DocumentMetadatas.FindAsync(documentId);
        if (document == null)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(document.StorageKey))
        {
            return NotFound("Document file not available (processing may be incomplete).");
        }

        var stream = await _mediaStorageService.DownloadFileAsync(document.StorageKey);
        stream.Position = 0;
        var contentType = string.IsNullOrWhiteSpace(document.ContentType)
            ? "application/octet-stream"
            : document.ContentType;

        if (contentType == "application/octet-stream")
        {
            var name = document.FileName ?? document.OriginalFileName ?? string.Empty;
            var ext = Path.GetExtension(name)?.ToLowerInvariant() ?? string.Empty;
            contentType = ext switch
            {
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".csv" => "text/csv",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                _ => contentType
            };
        }

        return File(stream, contentType);
    }

    /// <summary>
    /// Delete a document and all its chunks
    /// </summary>
    [HttpDelete("{documentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(Guid documentId)
    {
        var document = await _context.DocumentMetadatas
            .Include(d => d.DocumentGroups)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return NotFound();

        _context.DocumentMetadatas.Remove(document);
        await _context.SaveChangesAsync();

        if (document.DocumentGroups.Any())
        {
            await InvalidateGroupsCache();
            await NotifyGroupsChangedAsync();
        }

        return NoContent();
    }

    /// <summary>
    /// Find the location of specific text within a document
    /// </summary>
    [HttpPost("{documentId}/find-location")]
    [ProducesResponseType(typeof(FindLocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FindTextLocation(Guid documentId, [FromBody] FindLocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SearchText))
            return BadRequest("SearchText is required");

        var document = await _context.DocumentMetadatas
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return NotFound(new { error = "Document not found" });

        var locations = new List<TextLocation>();

        foreach (var chunk in document.Chunks)
        {
            if (chunk.ChunkText.Contains(request.SearchText, StringComparison.OrdinalIgnoreCase))
            {
                // Calculate position description
                var positionDescription = GetPositionDescription(chunk.X, chunk.Y, chunk.Width, chunk.Height);
                
                locations.Add(new TextLocation
                {
                    DocumentId = documentId,
                    DocumentName = document.FileName,
                    ChunkId = chunk.Id,
                    PageNumber = chunk.PageNumber,
                    X = chunk.X,
                    Y = chunk.Y,
                    Width = chunk.Width,
                    Height = chunk.Height,
                    Position = positionDescription,
                    ContextText = GetContextAroundText(chunk.ChunkText, request.SearchText, 50)
                });
            }
        }

        return Ok(new FindLocationResponse
        {
            DocumentId = documentId,
            DocumentName = document.FileName,
            SearchText = request.SearchText,
            MatchCount = locations.Count,
            Locations = locations
        });
    }

    private string GetPositionDescription(double? x, double? y, double? width, double? height)
    {
        if (!x.HasValue || !y.HasValue)
            return "position unknown";

        // Determine horizontal position
        string horizontal;
        if (x.Value < PdfConstants.PageWidth / 3)
            horizontal = "left";
        else if (x.Value < 2 * PdfConstants.PageWidth / 3)
            horizontal = "center";
        else
            horizontal = "right";

        // Determine vertical position (Y is from bottom in PDF coordinates)
        string vertical;
        if (y.Value < PdfConstants.PageHeight / 3)
            vertical = "bottom";
        else if (y.Value < 2 * PdfConstants.PageHeight / 3)
            vertical = "middle";
        else
            vertical = "top";

        return $"{vertical} {horizontal}";
    }

    private string GetContextAroundText(string text, string searchText, int contextChars)
    {
        var index = text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
            return text.Length > 100 ? text.Substring(0, 100) + "..." : text;

        var startIndex = Math.Max(0, index - contextChars);
        var endIndex = Math.Min(text.Length, index + searchText.Length + contextChars);
        
        var prefix = startIndex > 0 ? "..." : "";
        var suffix = endIndex < text.Length ? "..." : "";
        
        return prefix + text.Substring(startIndex, endIndex - startIndex) + suffix;
    }

    /// <summary>
    /// Reprocess failed or unprocessed documents
    /// </summary>
    [HttpPost("reprocess")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReprocessFailedDocuments()
    {
        try
        {
            var reprocessedCount = await _ragService.ReprocessFailedDocumentsAsync();
            return Ok(new { reprocessedCount, message = $"Reprocessed {reprocessedCount} documents" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reprocessing documents");
            return StatusCode(500, new { error = "Failed to reprocess documents", details = ex.Message });
        }
    }

    /// <summary>
    /// Get list of unprocessed documents
    /// </summary>
    [HttpGet("unprocessed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnprocessedDocuments()
    {
        try
        {
            var docs = await _ragService.GetUnprocessedDocumentsAsync();
            return Ok(docs.Select(d => new
            {
                d.Id,
                d.FileName,
                d.UploadedAt,
                d.ProcessingError,
                d.IsProcessed
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unprocessed documents");
            return StatusCode(500, new { error = "Failed to get unprocessed documents", details = ex.Message });
        }
    }

    /// <summary>
    /// Move a document to different groups (reindexing)
    /// </summary>
    [HttpPost("{id}/move")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoveDocumentToGroups(Guid id, [FromBody] MoveDocumentRequest request)
    {
        try
        {
            var document = await _context.DocumentMetadatas
                .Include(d => d.DocumentGroups)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
                return NotFound();

            // Remove existing group associations
            _context.DocumentGroups.RemoveRange(document.DocumentGroups);

            // Add new group associations
            if (request.GroupIds != null && request.GroupIds.Any())
            {
                foreach (var groupId in request.GroupIds)
                {
                    document.DocumentGroups.Add(new DocumentGroup
                    {
                        DocumentMetadataId = id,
                        ResourceGroupId = groupId
                    });
                }
            }

            await _context.SaveChangesAsync();

            await InvalidateGroupsCache();
            await NotifyGroupsChangedAsync();

            // Optionally regenerate embeddings if requested
            if (request.RegenerateEmbeddings)
            {
                _logger.LogInformation("Regenerating embeddings for moved document {DocumentId}", id);
                await _ragService.GenerateEmbeddingsAsync(id);
            }

            return Ok(new
            {
                message = $"Document moved to {request.GroupIds?.Count ?? 0} groups",
                regeneratedEmbeddings = request.RegenerateEmbeddings
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving document {DocumentId}", id);
            return StatusCode(500, new { error = "Failed to move document", details = ex.Message });
        }
    }

    private Task NotifyGroupsChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("groupsChanged")
            : Task.CompletedTask;
    }

    private async Task InvalidateGroupsCache(ApplicationDbContext? ctx = null)
    {
        try
        {
            var db = ctx ?? _context;
            var totalCount = await db.ResourceGroups.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)DefaultPageSize);

            var tasks = new List<Task>();
            for (int page = 1; page <= totalPages + 1; page++)
            {
                tasks.Add(_cacheService.RemoveAsync($"admin:groups:page:{page}:size:{DefaultPageSize}"));
                tasks.Add(_cacheService.RemoveAsync($"admin:groups:page:{page}:size:{MaxPageSize}"));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating groups cache");
        }
    }
}

public class QueryRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TopK { get; set; }
    public List<Guid>? GroupIds { get; set; }
    public List<Guid>? DocumentIds { get; set; }
    public Guid? ConversationId { get; set; }
    /// <summary>Optional client-provided request ID used for progress tracking via SignalR.</summary>
    public string? RequestId { get; set; }
}

public class MoveDocumentRequest
{
    public List<Guid>? GroupIds { get; set; }
    public bool RegenerateEmbeddings { get; set; } = false;
}

public class HighlightRequest
{
    public List<Guid>? ChunkIds { get; set; }
    public string? SearchText { get; set; }
}
