using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Hubs;

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
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

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
        _groupConversationMappingService = groupConversationMappingService;
        _cacheService = cacheService ?? new InMemoryCacheService(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<InMemoryCacheService>());
        _resourceHub = resourceHub;
    }

    /// <summary>
    /// Upload a new document for processing and indexing
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(CreatedAtActionResult), StatusCodes.Status201Created)]
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
            // Read uploaded file into memory
            var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;

            _logger.LogInformation("Upload received: {FileName}, {Size} bytes", originalFileName, ms.Length);

            // Convert to PDF synchronously (no background task — avoids all scope/disposal issues)
            var (pdfStream, pdfFileName) = await _processingService.ConvertToPdfAsync(ms, originalFileName, originalContentType, requestId);
            pdfStream.Position = 0;

            _logger.LogInformation("Converted to PDF: {PdfName}, {Size} bytes", pdfFileName, pdfStream.Length);

            if (pdfStream.Length == 0)
            {
                await _monitoringService.CompleteRequest(requestId, false, error: "PDF conversion produced empty stream");
                return StatusCode(500, new { error = "PDF conversion failed: empty stream" });
            }

            var documentId = Guid.NewGuid();
            var uploadKey = $"uploads/{documentId}.pdf";

            // Upload to S3 synchronously before writing DB record
            await _mediaStorageService.UploadFileAsync(uploadKey, pdfStream, "application/pdf");

            _logger.LogInformation("Uploaded to S3: {Key}", uploadKey);

            // Persist metadata
            var metadata = new DocumentMetadata
            {
                Id = documentId,
                FileName = pdfFileName,
                OriginalFileName = originalFileName,
                ContentType = "application/pdf",
                StorageKey = uploadKey,
                UploadedAt = DateTime.UtcNow,
                FileSize = pdfStream.Length,
                DocumentType = "pdf",
                IsProcessed = false
            };
            _context.DocumentMetadatas.Add(metadata);

            // Also create a ManagedFile so the upload appears in the admin files panel
            var managedFile = new ManagedFile
            {
                Id = documentId,
                FileName = pdfFileName,
                Label = originalFileName,
                ContentType = "application/pdf",
                StorageKey = uploadKey,
                UploadedAt = DateTime.UtcNow,
                FileSize = pdfStream.Length
            };
            _context.ManagedFiles.Add(managedFile);

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

            await _monitoringService.CompleteRequest(requestId, true, $"Document {pdfFileName} uploaded to S3");

            // Fire Lambda dispatch in background (non-critical — Lambda processes for RAG/embeddings)
            _ = Task.Run(async () =>
            {
                // Lambda will pick up the file from S3 and process it — no further action needed here
                await Task.CompletedTask;
            });

            return CreatedAtAction(nameof(UploadDocument), new { id = documentId }, new { id = documentId, requestId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document {FileName}", originalFileName);
            await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new { error = "Failed to upload document", details = ex.Message });
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
        try
        {
            _logger.LogInformation("Starting monitoring request...");
            // Use the client-provided RequestId for progress tracking via SignalR, otherwise generate one
            var clientRequestId = !string.IsNullOrWhiteSpace(request.RequestId) ? request.RequestId : null;
            requestId = await _monitoringService.StartRequest("DocumentQuery", 
                $"Query: {request.Query.Substring(0, Math.Min(50, request.Query.Length))}...",
                clientRequestId);
            _logger.LogInformation("Monitoring request started with ID: {RequestId}", requestId);

            _logger.LogInformation("Updating request status to 'Searching documents'...");
            await _monitoringService.UpdateRequestStatus(requestId, "Searching documents...");

            // Detect or create a conversation to maintain context within groups
            var groupId = request.GroupIds?.FirstOrDefault();
            Conversation? conversation = null;
            if (request.ConversationId.HasValue)
            {
                conversation = await _conversationService.GetConversationAsync(request.ConversationId.Value);
                if (conversation == null)
                {
                    _logger.LogWarning("Conversation {ConversationId} not found, falling back to detection", request.ConversationId);
                }
            }

            // If no conversation explicitly provided, consult global mapping for the selected group
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

                // Detect topic change and possibly switch to a new conversation automatically
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

            // Add user message to conversation
                await _conversationService.AddMessageAsync(conversation.Id, "user", request.Query, null, request.DocumentIds);

            // In-process fallback: if specific documents were requested but haven't been processed
            // by Lambda yet (IsProcessed=false, no EmbeddingVector chunks), process them now so the
            // query has something to work with. This handles the race between upload and query.
            //
            // IMPORTANT: Run each document through a fresh DI scope so the processing service gets
            // its own ApplicationDbContext. The controller's _context already has these entities
            // tracked (Unchanged). Sharing the same context causes 'Unexpected entry.EntityState:
            // Detached' when DocumentProcessingService tries to set metadata.Chunks on the
            // already-tracked entity.
            if (request.DocumentIds != null && request.DocumentIds.Any())
            {
                // Use a plain list of IDs to avoid passing tracked entities across scope boundaries
                var unprocessedIds = await _context.DocumentMetadatas
                    .Where(d => request.DocumentIds.Contains(d.Id) && !d.IsProcessed && d.StorageKey != null)
                    .Select(d => new { d.Id, d.FileName, d.StorageKey })
                    .ToListAsync();

                if (unprocessedIds.Any())
                {
                    _logger.LogInformation("Documents not yet processed by Lambda: {Ids}. Running in-process fallback.",
                        string.Join(", ", unprocessedIds.Select(d => d.Id)));
                    await _monitoringService.UpdateRequestStatus(requestId!, "Processing",
                        $"Processing {unprocessedIds.Count} document(s) in-process (Lambda not yet complete)", 35);

                    foreach (var doc in unprocessedIds)
                    {
                        try
                        {
                            // Fresh scope = fresh DbContext: no tracking conflicts with the controller's context
                            using var fallbackScope = _scopeFactory.CreateScope();
                            var scopedProcessing = fallbackScope.ServiceProvider.GetRequiredService<IDocumentProcessingService>();
                            var scopedRag = fallbackScope.ServiceProvider.GetRequiredService<IRAGService>();
                            using var pdfStream = await _mediaStorageService.DownloadFileAsync(doc.StorageKey!);
                            await scopedProcessing.ProcessDocumentAsync(pdfStream, doc.FileName, "application/pdf", requestId, doc.Id);
                            await scopedRag.GenerateEmbeddingsAsync(doc.Id);
                            _logger.LogInformation("In-process fallback complete for document {DocumentId}", doc.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "In-process fallback failed for document {DocumentId} — query will proceed without it", doc.Id);
                        }
                    }
                }
            }

            _logger.LogInformation("Calling RAG service QueryDocumentsWithConversationAsync...");
            var result = await _ragService.QueryDocumentsWithConversationAsync(
                request.Query,
                conversation.Id,
                request.TopK ?? 5,
                request.GroupIds,
                request.DocumentIds);
            _logger.LogInformation("RAG service returned result. Answer length: {Length}", result.Answer?.Length ?? 0);

            // Add assistant response to conversation
            await _conversationService.AddMessageAsync(conversation.Id, "assistant", result.Answer ?? string.Empty);

            // Update global Group -> Conversation mapping (global scope)
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

            await _monitoringService.CompleteRequest(requestId, true, 
                $"Found {result.Sources?.Count ?? 0} sources");
            _logger.LogInformation("=== DOCUMENT QUERY COMPLETED SUCCESSFULLY ===");

            // Persist a DocumentQuery history entry and attach the monitoring request id so UI can group
            try
            {
                var hist = new RequestHistory
                {
                    RequestType = "DocumentQuery",
                    InputText = request.Query,
                    Response = $"Found {result.Sources?.Count ?? 0} sources",
                    DetectedTask = "document_query",
                    CorrelationId = requestId
                };

                await _historyService.SaveRequestAsync(hist);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save document query history");
            }

            result.ConversationId = conversation.Id;
            return Ok(result);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Query timed out for query: {Query}", request.Query);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: "Request timed out");
            return StatusCode(504, new { error = "Request timed out", details = ex.Message });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Query was cancelled (client disconnected) for query: {Query}", request.Query);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: "Request cancelled by client");
            return StatusCode(499, new { error = "Request cancelled", details = "Client disconnected" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== ERROR QUERYING DOCUMENTS === Message: {Message}, StackTrace: {StackTrace}", 
                ex.Message, ex.StackTrace);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new { error = "Failed to query documents", details = ex.Message });
        }
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

        var totalCount = await _context.DocumentMetadatas.CountAsync();

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
