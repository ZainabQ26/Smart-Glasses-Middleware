using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Hubs;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Handles administrative operations for managing files, prompts, and resource groups
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ICacheService _cacheService;
    private readonly IRagService _ragService;
    private readonly IRAGService _documentRagService;
    private readonly ILogger<AdminController> _logger;
    private readonly IRequestMonitoringService _monitoringService;
    private readonly IHubContext<ResourceHub>? _resourceHub;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public AdminController(
        ApplicationDbContext context,
        IMediaStorageService mediaStorageService,
        ICacheService cacheService,
        IRagService ragService,
        IRAGService documentRagService,
        ILogger<AdminController> logger,
        IRequestMonitoringService monitoringService,
        IHubContext<ResourceHub>? resourceHub = null)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _cacheService = cacheService;
        _ragService = ragService;
        _documentRagService = documentRagService;
        _logger = logger;
        _monitoringService = monitoringService;
        _resourceHub = resourceHub;
    }

    /// <summary>
    /// Fix ManagedFile entries for already-processed documents so filenames and content-types reflect the converted PDF.
    /// </summary>
    [HttpPost("fix-processed-managedfiles")]
    public async Task<IActionResult> FixProcessedManagedFiles()
    {
        // Find processed documents with an associated ManagedFile
        var docs = await _context.DocumentMetadatas
            .Where(d => d.IsProcessed && d.ManagedFileId != null)
            .ToListAsync();

        int updated = 0;
        foreach (var doc in docs)
        {
            if (doc.ManagedFileId is not Guid mfId) continue;
            var mf = await _context.ManagedFiles.FindAsync(mfId);
            if (mf == null) continue;

            var desiredFileName = doc.FileName;
            if (!desiredFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Ensure PDF extension on metadata filename as a fallback
                desiredFileName = Path.GetFileNameWithoutExtension(desiredFileName) + ".pdf";
            }

            bool changed = false;
            if (!string.Equals(mf.FileName, desiredFileName, StringComparison.Ordinal))
            {
                mf.FileName = desiredFileName;
                changed = true;
            }

            if (!string.Equals(mf.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                mf.ContentType = "application/pdf";
                changed = true;
            }

            if (changed)
            {
                _context.ManagedFiles.Update(mf);
                updated++;
            }
        }

        if (updated > 0) await _context.SaveChangesAsync();

        return Ok(new { updated, total = docs.Count });
    }

    #region Files

    /// <summary>
    /// Get managed files with pagination and caching
    /// </summary>
    [HttpGet("files")]
    public async Task<ActionResult<object>> GetFiles([FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        // Validate pagination parameters
        if (pageSize < 1 || pageSize > MaxPageSize) pageSize = DefaultPageSize;
        if (page < 1) page = 1;

        var cacheKey = $"admin:files:page:{page}:size:{pageSize}";

        // Try to get from cache first
        var cachedResult = await _cacheService.GetAsync<object>(cacheKey);
        if (cachedResult != null)
        {
            return Ok(cachedResult);
        }

        var totalCount = await _context.ManagedFiles.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var files = await _context.ManagedFiles
            .Include(f => f.FileGroups)
            .ThenInclude(fg => fg.ResourceGroup)
            .OrderByDescending(f => f.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.Label,
                f.ContentType,
                f.StorageKey,
                f.UploadedAt,
                f.FileSize,
                Groups = f.FileGroups.Select(fg => new { fg.ResourceGroup.Id, fg.ResourceGroup.Name })
            })
            .ToListAsync();

        var result = new
        {
            Data = files,
            Pagination = new
            {
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = page < totalPages
            }
        };

        // Cache for 30 seconds
        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromSeconds(30));

        return Ok(result);
    }

    /// <summary>
    /// Upload a new file with label
    /// </summary>
    [HttpPost("files")]
    public async Task<ActionResult> UploadFile([FromForm] FileUploadRequest request)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "File is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Label is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Server-side validation: disallow audio files on the generic files endpoint.
        // Audio is allowed via prompt/media endpoints (e.g. /api/ask) but should not
        // be saved through the admin `files` upload flow.
        var disallowedAudioExtensions = new[] { ".wav", ".mp3", ".m4a", ".flac", ".aac" };
        var fileExt = Path.GetExtension(request.File.FileName ?? string.Empty).ToLowerInvariant();
        var fileContentType = (request.File.ContentType ?? string.Empty).ToLowerInvariant();
        if (disallowedAudioExtensions.Contains(fileExt) || fileContentType.StartsWith("audio/"))
        {
            var pdAudio = new ValidationProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Validation Failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Audio files are not allowed on this endpoint. Use the prompt/media endpoints for audio uploads.",
                Instance = HttpContext.Request.Path
            };
            pdAudio.Errors.Add("file", new[] { "Audio file uploads are not permitted via /api/admin/files" });
            pdAudio.Extensions["requestId"] = HttpContext.TraceIdentifier;
            pdAudio.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");

            return BadRequest(pdAudio);
        }

        string? requestId = null;
            try
            {
                requestId = await _monitoringService.StartRequest("FileUpload", 
                    $"Uploading: {request.File.FileName ?? string.Empty} (Label: {request.Label})");
            await _monitoringService.UpdateRequestStatus(requestId, "Uploading file...");

            // Compute content hash for deduplication
            using var hashStream = request.File.OpenReadStream();
            var contentHash = _mediaStorageService.ComputeContentHash(hashStream);

            // Check if file with same hash already exists
            var existingFile = await _context.ManagedFiles
                .FirstOrDefaultAsync(f => f.ContentHash == contentHash);

            if (existingFile != null)
            {
                _logger.LogInformation("File with hash {Hash} already exists as {FileId}, updating timestamp and returning existing file", contentHash, existingFile.Id);

                // Refresh the uploaded timestamp so repeated uploads surface in the admin listing
                existingFile.UploadedAt = DateTime.UtcNow;
                // Optionally update the label to reflect the most recent upload label
                existingFile.Label = request.Label ?? existingFile.Label;
                _context.ManagedFiles.Update(existingFile);
                await _context.SaveChangesAsync();

                // Invalidate caches and notify listeners so UI and API listings reflect the update immediately
                try { await InvalidateFilesCache(); } catch { /* best-effort */ }
                try { await NotifyFilesChangedAsync(); } catch { /* best-effort */ }

                await _monitoringService.CompleteRequest(requestId, true, "File already exists (deduplicated, refreshed)");
                return Ok(new
                {
                    existingFile.Id,
                    existingFile.FileName,
                    existingFile.Label,
                    existingFile.ContentType,
                    existingFile.StorageKey,
                    existingFile.UploadedAt,
                    existingFile.FileSize,
                    existingFile.ContentHash,
                    deduplicated = true
                });
            }

            await _monitoringService.UpdateRequestStatus(requestId, "Storing file...");
            // Upload to storage
            using var stream = request.File.OpenReadStream();
            var contentType = request.File.ContentType ?? "application/octet-stream";
            var storageKey = await _mediaStorageService.UploadMediaAsync(stream, request.File.FileName ?? string.Empty, contentType);

            // Save to database
                var managedFile = new ManagedFile
            {
                Id = Guid.NewGuid(),
                    FileName = request.File.FileName ?? string.Empty,
                Label = string.IsNullOrWhiteSpace(request.Label) ? (request.File.FileName ?? "Untitled") : request.Label,
                ContentType = contentType,
                StorageKey = storageKey,
                ContentHash = contentHash,
                UploadedAt = DateTime.UtcNow,
                FileSize = request.File.Length
            };

            _context.ManagedFiles.Add(managedFile);
            await _context.SaveChangesAsync();

            await _monitoringService.UpdateRequestStatus(requestId, "Processing for RAG...");
            // Process file for RAG indexing
            try
            {
                await _ragService.ProcessFileForRagAsync(
                    managedFile.Id,
                    managedFile.FileName,
                    managedFile.ContentType,
                    managedFile.StorageKey);
                _logger.LogInformation("File {FileId} processed for RAG", managedFile.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file {FileId} for RAG, but file was uploaded successfully", managedFile.Id);
            }

            // Invalidate cache
            await InvalidateFilesCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"File uploaded successfully: {managedFile.FileName}");

            await NotifyFilesChangedAsync();

            return CreatedAtAction(nameof(GetFiles), new { id = managedFile.Id }, new
            {
                managedFile.Id,
                managedFile.FileName,
                managedFile.Label,
                managedFile.ContentType,
                managedFile.StorageKey,
                managedFile.UploadedAt,
                managedFile.FileSize,
                managedFile.ContentHash,
                deduplicated = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file");
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while uploading the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a managed file
    /// </summary>
    [HttpDelete("files/{id}")]
    public async Task<ActionResult> DeleteFile(Guid id)
    {
        var file = await _context.ManagedFiles.FindAsync(id);
        if (file == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"File with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("FileDelete", 
                $"Deleting file: {file.FileName}");
            
            // Delete from storage
            await _mediaStorageService.DeleteMediaAsync(file.StorageKey);

            // Delete from database
            _context.ManagedFiles.Remove(file);
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidateFilesCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"File deleted: {file.FileName}");
            await NotifyFilesChangedAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileId}", id);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while deleting the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Preview a managed file (inline display). Falls back to DocumentMetadatas if not found in ManagedFiles.
    /// </summary>
    [HttpGet("files/{id}/preview")]
    public async Task<ActionResult> PreviewFile(Guid id)
    {
        // Try ManagedFiles first
        var file = await _context.ManagedFiles.FindAsync(id);
        string storageKey;
        string fileName;
        string contentType;

        if (file != null)
        {
            storageKey = file.StorageKey;
            fileName = file.FileName;
            contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        }
        else
        {
            // Fall back to DocumentMetadatas (processed documents)
            var doc = await _context.DocumentMetadatas.FindAsync(id);
            if (doc == null)
                return NotFound();
            storageKey = doc.StorageKey;
            fileName = doc.FileName;
            contentType = string.IsNullOrWhiteSpace(doc.ContentType) ? "application/octet-stream" : doc.ContentType;
        }

        try
        {
            var stream = await _mediaStorageService.DownloadMediaAsync(storageKey);
            stream.Position = 0;

            if (contentType == "application/octet-stream")
            {
                var ext = Path.GetExtension(fileName ?? string.Empty)?.ToLowerInvariant() ?? string.Empty;
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

            // Read fully so Content-Length is exact.
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            // inline disposition = browser displays (not downloads); filename = PDF viewer tab title.
            var safeFileName = (fileName ?? "document").Replace("\"", "");
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{safeFileName}\"";
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            return File(bytes, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing file {FileId}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Download a managed file (force download). Falls back to DocumentMetadatas if not found in ManagedFiles.
    /// </summary>
    [HttpGet("files/{id}/download")]
    public async Task<ActionResult> DownloadFile(Guid id)
    {
        string storageKey;
        string fileName;
        string contentType;

        var file = await _context.ManagedFiles.FindAsync(id);
        if (file != null)
        {
            storageKey = file.StorageKey;
            fileName = file.FileName;
            contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        }
        else
        {
            var doc = await _context.DocumentMetadatas.FindAsync(id);
            if (doc == null)
                return NotFound();
            storageKey = doc.StorageKey;
            fileName = doc.OriginalFileName ?? doc.FileName;
            contentType = string.IsNullOrWhiteSpace(doc.ContentType) ? "application/octet-stream" : doc.ContentType;
        }

        try
        {
            var stream = await _mediaStorageService.DownloadMediaAsync(storageKey);
            stream.Position = 0;

            if (contentType == "application/octet-stream")
            {
                var ext = Path.GetExtension(fileName ?? string.Empty)?.ToLowerInvariant() ?? string.Empty;
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

            // Force download with filename
            return File(stream, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileId}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Debug: returns size, first 16 bytes (hex), stored content-type and storage key for a file.
    /// </summary>
    [HttpGet("files/{id}/info")]
    public async Task<IActionResult> FileInfo(Guid id)
    {
        string storageKey; string fileName; string storedContentType;
        var file = await _context.ManagedFiles.FindAsync(id);
        if (file != null) { storageKey = file.StorageKey; fileName = file.FileName; storedContentType = file.ContentType; }
        else
        {
            var doc = await _context.DocumentMetadatas.FindAsync(id);
            if (doc == null) return NotFound(new { error = "Not found in ManagedFiles or DocumentMetadatas", id });
            storageKey = doc.StorageKey; fileName = doc.FileName; storedContentType = doc.ContentType;
        }
        try
        {
            var stream = await _mediaStorageService.DownloadMediaAsync(storageKey);
            stream.Position = 0;
            var header = new byte[Math.Min(16, stream.Length)];
            await stream.ReadExactlyAsync(header);
            return Ok(new { id, storageKey, fileName, storedContentType, totalBytes = stream.Length, firstBytesHex = Convert.ToHexString(header), firstBytesAscii = System.Text.Encoding.ASCII.GetString(header).Replace("\0","·") });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    #endregion

    #region Maintenance

    /// <summary>
    /// Reindex all documents' embeddings. Use `force=true` to re-embed already processed documents.
    /// </summary>
    [HttpPost("reindex")]
    public async Task<ActionResult> ReindexAll([FromQuery] bool force = false)
    {
        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("DocumentReindex", 
                $"Reindexing all documents (force: {force})");
            await _monitoringService.UpdateRequestStatus(requestId, "Reindexing documents...");
            
            var reindexed = await _documentRagService.ReindexAllDocumentsAsync(force);
            
            // Invalidate relevant caches
            await InvalidateFilesCache();
            
            await _monitoringService.CompleteRequest(requestId, true, 
                $"Reindexed {reindexed} documents");
            return Ok(new { reindexed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reindex documents");
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Reindex Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    #endregion

    #region Prompts

    /// <summary>
    /// Get managed prompts with pagination and caching
    /// </summary>
    [HttpGet("prompts")]
    public async Task<ActionResult<object>> GetPrompts([FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > MaxPageSize) pageSize = DefaultPageSize;

        var cacheKey = $"admin:prompts:page:{page}:size:{pageSize}";

        // Try to get from cache first
        var cachedResult = await _cacheService.GetAsync<object>(cacheKey);
        if (cachedResult != null)
        {
            return Ok(cachedResult);
        }

        var totalCount = await _context.ManagedPrompts.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var prompts = await _context.ManagedPrompts
            .Include(p => p.PromptGroups)
            .ThenInclude(pg => pg.ResourceGroup)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.PromptText,
                p.CreatedAt,
                p.UpdatedAt,
                Groups = p.PromptGroups.Select(pg => new { pg.ResourceGroup.Id, pg.ResourceGroup.Name })
            })
            .ToListAsync();

        var result = new
        {
            Data = prompts,
            Pagination = new
            {
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = page < totalPages
            }
        };

        // Cache for 30 seconds
        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromSeconds(30));

        return Ok(result);
    }

    /// <summary>
    /// Create a new prompt
    /// </summary>
    [HttpPost("prompts")]
    public async Task<ActionResult> CreatePrompt([FromBody] PromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.PromptText))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Prompt text is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("PromptCreate", 
                $"Creating prompt: {request.Name}");
            
            var prompt = new ManagedPrompt
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                PromptText = request.PromptText,
                CreatedAt = DateTime.UtcNow
            };

            _context.ManagedPrompts.Add(prompt);
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidatePromptsCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"Prompt created: {prompt.Name}");

            return CreatedAtAction(nameof(GetPrompts), new { id = prompt.Id }, new
            {
                prompt.Id,
                prompt.Name,
                prompt.PromptText,
                prompt.CreatedAt,
                prompt.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating prompt");
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while creating the prompt",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Update an existing prompt
    /// </summary>
    [HttpPut("prompts/{id}")]
    public async Task<ActionResult> UpdatePrompt(Guid id, [FromBody] PromptRequest request)
    {
        var prompt = await _context.ManagedPrompts.FindAsync(id);
        if (prompt == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Prompt with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.PromptText))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Prompt text is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("PromptUpdate", 
                $"Updating prompt: {request.Name}");
            
            prompt.Name = request.Name;
            prompt.PromptText = request.PromptText;
            prompt.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidatePromptsCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"Prompt updated: {prompt.Name}");

            return Ok(new
            {
                prompt.Id,
                prompt.Name,
                prompt.PromptText,
                prompt.CreatedAt,
                prompt.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating prompt {PromptId}", id);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while updating the prompt",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a prompt
    /// </summary>
    [HttpDelete("prompts/{id}")]
    public async Task<ActionResult> DeletePrompt(Guid id)
    {
        var prompt = await _context.ManagedPrompts.FindAsync(id);
        if (prompt == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Prompt with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("PromptDelete", 
                $"Deleting prompt: {prompt.Name}");
            
            _context.ManagedPrompts.Remove(prompt);
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidatePromptsCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"Prompt deleted: {prompt.Name}");
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting prompt {PromptId}", id);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while deleting the prompt",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    #endregion

    #region Resource Groups

    /// <summary>
    /// Get resource groups with pagination and caching
    /// </summary>
    [HttpGet("groups")]
    public async Task<ActionResult<object>> GetGroups([FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > MaxPageSize) pageSize = DefaultPageSize;

        var cacheKey = $"admin:groups:page:{page}:size:{pageSize}";

        // Try to get from cache first
        var cachedResult = await _cacheService.GetAsync<object>(cacheKey);
        if (cachedResult != null)
        {
            return Ok(cachedResult);
        }

        var totalCount = await _context.ResourceGroups.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var groups = await _context.ResourceGroups
            .Include(g => g.FileGroups)
            .ThenInclude(fg => fg.ManagedFile)
            .Include(g => g.PromptGroups)
            .ThenInclude(pg => pg.ManagedPrompt)
            .Include(g => g.DocumentGroups)
            .ThenInclude(dg => dg.DocumentMetadata)
            .Include(g => g.Conversations)
            .ThenInclude(c => c.Messages)
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var groupData = groups.Select(g => new
            {
                g.Id,
                g.Name,
                g.Description,
                g.CreatedAt,
                Files = g.FileGroups.Select(fg => new
                {
                    fg.ManagedFile.Id,
                    fg.ManagedFile.FileName,
                    fg.ManagedFile.Label
                }).ToList(),
                Prompts = g.PromptGroups.Select(pg => new
                {
                    pg.ManagedPrompt.Id,
                    pg.ManagedPrompt.Name
                }).ToList(),
                Documents = g.DocumentGroups.Select(dg => new
                {
                    dg.DocumentMetadata.Id,
                    dg.DocumentMetadata.FileName,
                    dg.DocumentMetadata.OriginalFileName,
                    dg.DocumentMetadata.ContentType
                }).ToList(),
                Conversations = g.Conversations.Select(c => new
                {
                    c.Id,
                    c.Title,
                    c.CreatedAt,
                    c.UpdatedAt,
                    c.IsActive,
                    MessageCount = c.Messages.Count
                }).ToList(),
                // Count reminders that reference this group
                Reminders = _context.ResourceGroups
                    .Where(rg => rg.Description != null && rg.Description.ToLower() == "reminder")
                    .Where(rg => rg.Conversations.Any(c => c.ResourceGroupId == g.Id))
                    .Select(rg => new
                    {
                        rg.Id,
                        rg.Name
                    }).ToList()
            })
            .ToList();

        var result = new
        {
            Data = groupData,
            Pagination = new
            {
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = page < totalPages
            }
        };

        // Cache for 30 seconds
        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromSeconds(30));

        return Ok(result);
    }

    /// <summary>
    /// Create a new resource group
    /// </summary>
    [HttpPost("groups")]
    public async Task<ActionResult> CreateGroup([FromBody] GroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("GroupCreate", 
                $"Creating group: {request.Name}");
            
            var group = new ResourceGroup
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                CreatedAt = DateTime.UtcNow
            };

            _context.ResourceGroups.Add(group);
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidateGroupsCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"Group created: {group.Name}");

            await NotifyGroupsChangedAsync();

            return CreatedAtAction(nameof(GetGroups), new { id = group.Id }, new
            {
                group.Id,
                group.Name,
                group.Description,
                group.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group");
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while creating the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Update a resource group
    /// </summary>
    [HttpPut("groups/{id}")]
    public async Task<ActionResult> UpdateGroup(Guid id, [FromBody] GroupRequest request)
    {
        var group = await _context.ResourceGroups.FindAsync(id);
        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("GroupUpdate", 
                $"Updating group: {request.Name}");
            
            group.Name = request.Name;
            group.Description = request.Description;

            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidateGroupsCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"Group updated: {group.Name}");

            await NotifyGroupsChangedAsync();

            return Ok(new
            {
                group.Id,
                group.Name,
                group.Description,
                group.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating group {GroupId}", id);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while updating the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a resource group
    /// </summary>
    [HttpDelete("groups/{id}")]
    public async Task<ActionResult> DeleteGroup(Guid id)
    {
        var group = await _context.ResourceGroups.FindAsync(id);
        if (group == null)
        {
            // Idempotent delete to avoid UI errors on duplicate clicks or race conditions
            return NoContent();
        }

        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("GroupDelete", 
                $"Deleting group: {group.Name}");

            // Cascade-delete linked conversations and their messages before removing the group.
            // (Conversation.ResourceGroupId has OnDelete=SetNull so EF won't auto-delete them.)
            var conversationIds = await _context.Conversations
                .Where(c => c.ResourceGroupId == id)
                .Select(c => c.Id)
                .ToListAsync();

            if (conversationIds.Count > 0)
            {
                await _context.ChatMessageAttachments
                    .Where(a => conversationIds.Contains(a.ChatMessage.ConversationId))
                    .ExecuteDeleteAsync();
                await _context.ChatMessages
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .ExecuteDeleteAsync();
                await _context.GroupConversationMappings
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .ExecuteDeleteAsync();
                await _context.Conversations
                    .Where(c => conversationIds.Contains(c.Id))
                    .ExecuteDeleteAsync();
            }
            else
            {
                // Clean up any orphaned mapping entries for this group even with no conversations
                await _context.GroupConversationMappings
                    .Where(m => m.ResourceGroupId == id)
                    .ExecuteDeleteAsync();
            }

            // Delete linked DocumentMetadata entries that belong only to this group
            var soloDocIds = await _context.DocumentGroups
                .Where(dg => dg.ResourceGroupId == id)
                .GroupBy(dg => dg.DocumentMetadataId)
                .Where(g2 => g2.Count() == 1)  // only linked to this one group
                .Select(g2 => g2.Key)
                .ToListAsync();

            if (soloDocIds.Count > 0)
            {
                await _context.DocumentChunks
                    .Where(c => soloDocIds.Contains(c.DocumentMetadataId))
                    .ExecuteDeleteAsync();
                await _context.DocumentGroups
                    .Where(dg => soloDocIds.Contains(dg.DocumentMetadataId))
                    .ExecuteDeleteAsync();
                await _context.DocumentMetadatas
                    .Where(d => soloDocIds.Contains(d.Id))
                    .ExecuteDeleteAsync();
            }
            
            _context.ResourceGroups.Remove(group);
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidateGroupsCache();

            await _monitoringService.CompleteRequest(requestId, true, 
                $"Group deleted: {group.Name}");
            await NotifyGroupsChangedAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting group {GroupId}", id);
            if (requestId != null)
                await _monitoringService.CompleteRequest(requestId, false, error: ex.Message);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while deleting the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get detailed information about a specific group including all its contents
    /// </summary>
    [HttpGet("groups/{id}")]
    public async Task<ActionResult<object>> GetGroupDetails(Guid id)
    {
        var group = await _context.ResourceGroups
            .Include(g => g.FileGroups)
            .ThenInclude(fg => fg.ManagedFile)
            .Include(g => g.PromptGroups)
            .ThenInclude(pg => pg.ManagedPrompt)
            .Include(g => g.Conversations)
            .ThenInclude(c => c.Messages)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        var result = new
        {
            group.Id,
            group.Name,
            group.Description,
            group.CreatedAt,
            Files = group.FileGroups.Select(fg => new
            {
                fg.ManagedFile.Id,
                fg.ManagedFile.FileName,
                fg.ManagedFile.Label,
                fg.ManagedFile.ContentType,
                fg.ManagedFile.FileSize,
                fg.ManagedFile.UploadedAt
            }).ToList(),
            Prompts = group.PromptGroups.Select(pg => new
            {
                pg.ManagedPrompt.Id,
                pg.ManagedPrompt.Name,
                pg.ManagedPrompt.PromptText
            }).ToList(),
            Conversations = group.Conversations.Select(c => new
            {
                c.Id,
                c.Title,
                c.CreatedAt,
                c.UpdatedAt,
                c.IsActive,
                MessageCount = c.Messages.Count,
                Messages = c.Messages.OrderBy(m => m.Timestamp).Select(m => new
                {
                    m.Id,
                    m.Role,
                    Content = m.Content.Length > 100 ? m.Content.Substring(0, 100) + "..." : m.Content,
                    m.Timestamp
                }).ToList()
            }).ToList(),
            // Find reminders (groups with description="Reminder") that have conversations pointing to this group
            Reminders = _context.ResourceGroups
                .Where(rg => rg.Description != null && rg.Description.ToLower() == "reminder")
                .Where(rg => rg.Conversations.Any(c => c.ResourceGroupId == group.Id))
                .Select(rg => new
                {
                    rg.Id,
                    rg.Name,
                    rg.Description,
                    rg.CreatedAt,
                    FileCount = rg.FileGroups.Count,
                    ConversationCount = rg.Conversations.Count
                }).ToList()
        };

        return Ok(result);
    }

    /// <summary>
    /// Add a file to a group
    /// </summary>
    [HttpPost("groups/{groupId}/files/{fileId}")]
    public async Task<ActionResult> AddFileToGroup(Guid groupId, Guid fileId)
    {
        var group = await _context.ResourceGroups.FindAsync(groupId);
        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {groupId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        var file = await _context.ManagedFiles.FindAsync(fileId);
        if (file == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"File with ID {fileId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            var existing = await _context.Set<ManagedFileGroup>()
                .FirstOrDefaultAsync(fg => fg.ManagedFileId == fileId && fg.ResourceGroupId == groupId);

            if (existing != null)
            {
                return Ok(new { message = "File is already in the group" });
            }

            var fileGroup = new ManagedFileGroup
            {
                ManagedFileId = fileId,
                ResourceGroupId = groupId
            };

            _context.Set<ManagedFileGroup>().Add(fileGroup);
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidateAllCaches();

            await NotifyGroupsChangedAsync();
            await NotifyFilesChangedAsync();

            return Ok(new { message = "File added to group successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding file {FileId} to group {GroupId}", fileId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while adding the file to the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Remove a file from a group
    /// </summary>
    [HttpDelete("groups/{groupId}/files/{fileId}")]
    public async Task<ActionResult> RemoveFileFromGroup(Guid groupId, Guid fileId)
    {
        var fileGroup = await _context.Set<ManagedFileGroup>()
            .FirstOrDefaultAsync(fg => fg.ManagedFileId == fileId && fg.ResourceGroupId == groupId);

        if (fileGroup == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = "File is not in the specified group",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            _context.Set<ManagedFileGroup>().Remove(fileGroup);
            await _context.SaveChangesAsync();
            
            // Check if group is now empty and auto-delete
            await AutoDeleteEmptyGroup(groupId);

            // Invalidate cache
            await InvalidateAllCaches();

            await NotifyGroupsChangedAsync();
            await NotifyFilesChangedAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing file {FileId} from group {GroupId}", fileId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while removing the file from the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Add a prompt to a group
    /// </summary>
    [HttpPost("groups/{groupId}/prompts/{promptId}")]
    public async Task<ActionResult> AddPromptToGroup(Guid groupId, Guid promptId)
    {
        var group = await _context.ResourceGroups.FindAsync(groupId);
        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {groupId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        var prompt = await _context.ManagedPrompts.FindAsync(promptId);
        if (prompt == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Prompt with ID {promptId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            var existing = await _context.Set<ManagedPromptGroup>()
                .FirstOrDefaultAsync(pg => pg.ManagedPromptId == promptId && pg.ResourceGroupId == groupId);

            if (existing != null)
            {
                return Ok(new { message = "Prompt is already in the group" });
            }

            var promptGroup = new ManagedPromptGroup
            {
                ManagedPromptId = promptId,
                ResourceGroupId = groupId
            };

            _context.Set<ManagedPromptGroup>().Add(promptGroup);
            await _context.SaveChangesAsync();

            // Invalidate cache
            await InvalidateAllCaches();

            await NotifyGroupsChangedAsync();

            return Ok(new { message = "Prompt added to group successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding prompt {PromptId} to group {GroupId}", promptId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while adding the prompt to the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Remove a prompt from a group
    /// </summary>
    [HttpDelete("groups/{groupId}/prompts/{promptId}")]
    public async Task<ActionResult> RemovePromptFromGroup(Guid groupId, Guid promptId)
    {
        var promptGroup = await _context.Set<ManagedPromptGroup>()
            .FirstOrDefaultAsync(pg => pg.ManagedPromptId == promptId && pg.ResourceGroupId == groupId);

        if (promptGroup == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = "Prompt is not in the specified group",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            _context.Set<ManagedPromptGroup>().Remove(promptGroup);
            await _context.SaveChangesAsync();
            
            // Check if group is now empty and auto-delete
            await AutoDeleteEmptyGroup(groupId);

            // Invalidate cache
            await InvalidateAllCaches();

            await NotifyGroupsChangedAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing prompt {PromptId} from group {GroupId}", promptId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while removing the prompt from the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    #endregion

    #region Helper Methods

    private Task NotifyFilesChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("filesChanged")
            : Task.CompletedTask;
    }

    private Task NotifyGroupsChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("groupsChanged")
            : Task.CompletedTask;
    }

    /// <summary>
    /// Auto-delete a group if it has no files, prompts, or conversations
    /// </summary>
    private async Task AutoDeleteEmptyGroup(Guid groupId)
    {
        try
        {
            var group = await _context.ResourceGroups
                .Include(g => g.FileGroups)
                .Include(g => g.PromptGroups)
                .Include(g => g.Conversations)
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
            {
                return; // Group already deleted
            }

            // Check if group is empty
            bool isEmpty = !group.FileGroups.Any() && 
                          !group.PromptGroups.Any() && 
                          !group.Conversations.Any();

            if (isEmpty)
            {
                _logger.LogInformation("Auto-deleting empty group: {GroupName} (ID: {GroupId})", group.Name, groupId);
                _context.ResourceGroups.Remove(group);
                await _context.SaveChangesAsync();
                await InvalidateGroupsCache();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-deleting empty group {GroupId}", groupId);
            // Don't throw - this is a best-effort cleanup
        }
    }

    /// <summary>
    /// Invalidate files cache for all pages
    /// </summary>
    private async Task InvalidateFilesCache()
    {
        try
        {
            // Invalidate cache pattern - remove all files cache keys in parallel.
            // Use Math.Max(..., 10) so that when called after bulk deletes (count = 0)
            // we still clear at least 10 pages worth of stale cache entries.
            var totalCount = await _context.ManagedFiles.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)DefaultPageSize);
            var maxPage = Math.Max(totalPages + 1, 10);

            var tasks = new List<Task>();
            for (int page = 1; page <= maxPage; page++)
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
    /// Invalidate prompts cache for all pages
    /// </summary>
    private async Task InvalidatePromptsCache()
    {
        try
        {
            var totalCount = await _context.ManagedPrompts.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)DefaultPageSize);
            var maxPage = Math.Max(totalPages + 1, 10);

            var tasks = new List<Task>();
            for (int page = 1; page <= maxPage; page++)
            {
                tasks.Add(_cacheService.RemoveAsync($"admin:prompts:page:{page}:size:{DefaultPageSize}"));
                tasks.Add(_cacheService.RemoveAsync($"admin:prompts:page:{page}:size:{MaxPageSize}"));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating prompts cache");
        }
    }

    /// <summary>
    /// Invalidate groups cache for all pages
    /// </summary>
    private async Task InvalidateGroupsCache()
    {
        try
        {
            var totalCount = await _context.ResourceGroups.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)DefaultPageSize);
            var maxPage = Math.Max(totalPages + 1, 10);

            var tasks = new List<Task>();
            for (int page = 1; page <= maxPage; page++)
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

    /// <summary>
    /// Invalidate all caches (files, prompts, groups)
    /// </summary>
    private async Task InvalidateAllCaches()
    {
        await Task.WhenAll(
            InvalidateFilesCache(),
            InvalidatePromptsCache(),
            InvalidateGroupsCache()
        );
    }

    #endregion

    #region Batch Operations

    /// <summary>
    /// Get files, prompts, and groups in a single batch request for efficiency
    /// </summary>
    [HttpGet("batch")]
    public async Task<ActionResult<object>> GetBatch(
        [FromQuery] bool includeFiles = true,
        [FromQuery] bool includePrompts = true,
        [FromQuery] bool includeGroups = true,
        [FromQuery] int maxItems = 1000)
    {
        try
        {
            var result = new Dictionary<string, object>();

            if (includeFiles)
            {
                var files = await _context.ManagedFiles
                    .Include(f => f.FileGroups)
                    .ThenInclude(fg => fg.ResourceGroup)
                    .OrderByDescending(f => f.UploadedAt)
                    .Take(maxItems)
                    .Select(f => new
                    {
                        f.Id,
                        f.FileName,
                        f.Label,
                        f.ContentType,
                        f.StorageKey,
                        f.UploadedAt,
                        f.FileSize,
                        Groups = f.FileGroups.Select(fg => new { fg.ResourceGroup.Id, fg.ResourceGroup.Name })
                    })
                    .ToListAsync();

                result["files"] = new
                {
                    data = files,
                    count = files.Count
                };
            }

            if (includePrompts)
            {
                var conversations = await _context.Conversations
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(maxItems)
                    .Select(c => new
                    {
                        c.Id,
                        c.Title,
                        c.ResourceGroupId,
                        c.CreatedAt,
                        c.UpdatedAt,
                        MessageCount = c.Messages.Count
                    })
                    .ToListAsync();

                result["prompts"] = new
                {
                    data = conversations,
                    count = conversations.Count
                };
            }

            if (includeGroups)
            {
                var groups = await _context.ResourceGroups
                    .Include(g => g.FileGroups)
                    .Include(g => g.Conversations)
                    .OrderByDescending(g => g.CreatedAt)
                    .Take(maxItems)
                    .Select(g => new
                    {
                        g.Id,
                        g.Name,
                        g.Description,
                        g.CreatedAt,
                        FileCount = g.FileGroups.Count,
                        ConversationCount = g.Conversations.Count
                    })
                    .ToListAsync();

                result["groups"] = new
                {
                    data = groups,
                    count = groups.Count
                };
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching batch data");
            return StatusCode(500, new { error = "Failed to fetch batch data", details = ex.Message });
        }
    }

    /// <summary>
    /// Clear ALL history data: request monitoring, files, documents, conversations, AI logs, and transcription jobs.
    /// Nothing is preserved — all user data is deleted.
    /// </summary>
    [HttpPost("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        _logger.LogWarning("ClearAll invoked — deleting all history data");
        var counts = new Dictionary<string, int>();

        try
        {
            // Clear monitoring in-memory state + RequestStatuses + RequestHistories
            await _monitoringService.ClearAll();
            counts["requestStatuses"] = 0; // already cleared by service

            // Delete conversation-related data (FK order: attachments → messages → group mappings → conversations)
            counts["chatMessageAttachments"] = await _context.ChatMessageAttachments.ExecuteDeleteAsync();
            counts["chatMessages"] = await _context.ChatMessages.ExecuteDeleteAsync();
            counts["groupConversationMappings"] = await _context.GroupConversationMappings.ExecuteDeleteAsync();
            counts["conversations"] = await _context.Conversations.ExecuteDeleteAsync();

            // Delete document-related data (FK order: chunks/groups → metadata)
            counts["documentChunks"] = await _context.DocumentChunks.ExecuteDeleteAsync();
            counts["documentGroups"] = await _context.DocumentGroups.ExecuteDeleteAsync();
            counts["documentMetadatas"] = await _context.DocumentMetadatas.ExecuteDeleteAsync();

            // Delete RAG entries and managed files
            counts["ragEntries"] = await _context.RagEntries.ExecuteDeleteAsync();
            counts["managedFiles"] = await _context.ManagedFiles.ExecuteDeleteAsync();

            // Delete AI interaction logs and transcription jobs
            counts["aiInteractionLogs"] = await _context.AIInteractionLogs.ExecuteDeleteAsync();
            counts["transcriptionJobs"] = await _context.TranscriptionJobs.ExecuteDeleteAsync();

            // Delete all prompt templates (ManagedPromptGroup join rows cascade from this)
            counts["managedPrompts"] = await _context.ManagedPrompts.ExecuteDeleteAsync();

            // Delete all resource groups (including reminders).
            // ManagedFiles are already gone (cascaded ManagedFileGroup entries).
            // DocumentGroups already deleted. Conversations already deleted.
            // ManagedPromptGroup entries already gone (cascaded from ManagedPrompts deletion above).
            counts["resourceGroups"] = await _context.ResourceGroups.ExecuteDeleteAsync();

            // Invalidate all caches so stale data doesn't surface
            await InvalidateAllCaches();

            // Notify connected clients that files/groups have changed
            await Task.WhenAll(
                NotifyFilesChangedAsync(),
                NotifyGroupsChangedAsync()
            );

            _logger.LogWarning("ClearAll completed. Rows deleted: {Counts}", counts);
            return Ok(new { success = true, deleted = counts });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearAll failed");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Clear All Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    #endregion
}
