using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;

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
    private readonly long _maxFileSize;

    public DocumentsController(
        ILogger<DocumentsController> logger,
        IDocumentProcessingService processingService,
        IRAGService ragService,
        IDocumentHighlightService highlightService,
        ApplicationDbContext context,
        IConfiguration configuration,
        IRequestMonitoringService monitoringService,
        IHistoryService historyService,
        IMediaStorageService mediaStorageService)
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
        var maxFileSizeMB = configuration.GetValue<int?>("RAG:MaxFileSizeMB") ?? 50;
        _maxFileSize = maxFileSizeMB * 1024 * 1024;
    }

    /// <summary>
    /// Upload and process a document (PDF, DOCX, TXT, or image)
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(DocumentMetadata), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadDocument(IFormFile file, [FromForm] List<Guid>? groupIds = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        // Validate file size
        if (file.Length > _maxFileSize)
            return BadRequest($"File size exceeds maximum allowed size of {_maxFileSize / 1024 / 1024}MB");

        try
        {
            using var stream = file.OpenReadStream();
            var document = await _processingService.ProcessDocumentAsync(stream, file.FileName, file.ContentType);

            // Add to groups if specified
            if (groupIds != null && groupIds.Any())
            {
                foreach (var groupId in groupIds)
                {
                    var group = await _context.ResourceGroups.FindAsync(groupId);
                    if (group != null)
                    {
                        document.DocumentGroups.Add(new DocumentGroup
                        {
                            DocumentMetadataId = document.Id,
                            ResourceGroupId = groupId
                        });
                    }
                    else
                    {
                        _logger.LogWarning("Group {GroupId} not found, skipping", groupId);
                    }
                }
                await _context.SaveChangesAsync();
            }

            // Generate embeddings for the document chunks (with retry logic)
            try
            {
                await _ragService.GenerateEmbeddingsAsync(document.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating embeddings for document {DocumentId}, will retry later", document.Id);
                // Don't fail the upload, background service will retry
            }

            return Ok(new
            {
                document.Id,
                document.FileName,
                document.DocumentType,
                document.TotalPages,
                document.IsProcessed,
                document.ProcessedAt,
                ChunkCount = document.Chunks.Count,
                Groups = document.DocumentGroups.Select(dg => dg.ResourceGroupId).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            return StatusCode(500, new { error = "Failed to process document", details = ex.Message });
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
            requestId = await _monitoringService.StartRequest("DocumentQuery", 
                $"Query: {request.Query.Substring(0, Math.Min(50, request.Query.Length))}...");
            _logger.LogInformation("Monitoring request started with ID: {RequestId}", requestId);

            _logger.LogInformation("Updating request status to 'Searching documents'...");
            await _monitoringService.UpdateRequestStatus(requestId, "Searching documents...");

            _logger.LogInformation("Calling RAG service QueryDocumentsAsync...");
            var result = await _ragService.QueryDocumentsAsync(
                request.Query,
                request.TopK ?? 5,
                request.GroupIds,
                request.DocumentIds);
            _logger.LogInformation("RAG service returned result. Answer length: {Length}", result.Answer?.Length ?? 0);

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
                d.DocumentType,
                d.TotalPages,
                d.FileSize,
                d.UploadedAt,
                d.IsProcessed,
                d.ProcessedAt,
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
            document.DocumentType,
            document.TotalPages,
            document.FileSize,
            document.UploadedAt,
            document.IsProcessed,
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

        var stream = await _mediaStorageService.DownloadFileAsync(document.StorageKey);
        stream.Position = 0;
        var contentType = string.IsNullOrWhiteSpace(document.ContentType)
            ? "application/octet-stream"
            : document.ContentType;

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
        var document = await _context.DocumentMetadatas.FindAsync(documentId);

        if (document == null)
            return NotFound();

        _context.DocumentMetadatas.Remove(document);
        await _context.SaveChangesAsync();

        return NoContent();
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
}

public class QueryRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TopK { get; set; }
    public List<Guid>? GroupIds { get; set; }
    public List<Guid>? DocumentIds { get; set; }
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
