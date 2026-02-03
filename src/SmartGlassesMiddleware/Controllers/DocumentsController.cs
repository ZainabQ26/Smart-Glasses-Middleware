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

    public DocumentsController(
        ILogger<DocumentsController> logger,
        IDocumentProcessingService processingService,
        IRAGService ragService,
        IDocumentHighlightService highlightService,
        ApplicationDbContext context)
    {
        _logger = logger;
        _processingService = processingService;
        _ragService = ragService;
        _highlightService = highlightService;
        _context = context;
    }

    /// <summary>
    /// Upload and process a document (PDF, DOCX, TXT, or image)
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(DocumentMetadata), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadDocument(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        try
        {
            using var stream = file.OpenReadStream();
            var document = await _processingService.ProcessDocumentAsync(stream, file.FileName, file.ContentType);

            // Generate embeddings for the document chunks
            await _ragService.GenerateEmbeddingsAsync(document.Id);

            return Ok(new
            {
                document.Id,
                document.FileName,
                document.DocumentType,
                document.TotalPages,
                document.IsProcessed,
                document.ProcessedAt,
                ChunkCount = document.Chunks.Count
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
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueryDocuments([FromBody] QueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest("Query cannot be empty");

        try
        {
            var response = await _ragService.QueryDocumentsAsync(request.Query, request.TopK ?? 5);
            var relevantChunks = await _ragService.SearchChunksAsync(request.Query, request.TopK ?? 5);

            return Ok(new QueryResponse
            {
                Answer = response,
                Sources = relevantChunks.Select(c => new DocumentSource
                {
                    DocumentId = c.DocumentMetadataId,
                    FileName = c.DocumentMetadata.FileName,
                    PageNumber = c.PageNumber,
                    ChunkId = c.Id,
                    ChunkText = c.ChunkText.Length > 200 ? c.ChunkText.Substring(0, 200) + "..." : c.ChunkText
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents");
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
}

public class QueryRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TopK { get; set; }
}

public class QueryResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<DocumentSource> Sources { get; set; } = new();
}

public class DocumentSource
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public Guid ChunkId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
}

public class HighlightRequest
{
    public List<Guid>? ChunkIds { get; set; }
    public string? SearchText { get; set; }
}
