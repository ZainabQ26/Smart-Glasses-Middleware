using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Hubs;
using System.Collections.Concurrent;
using System.Text;

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
    private readonly ILogger<AdminController> _logger;
    private readonly IRequestMonitoringService _monitoringService;
    private readonly IGroqTranscriptionLogService _groqTranscriptionLog;
    private readonly IHubContext<ResourceHub>? _resourceHub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOcrBatchService _ocrBatchService;
    private static readonly TimeSpan InitEndpointTimeout = TimeSpan.FromSeconds(8);
    private static readonly int UploadProcessingConcurrency = ResolveUploadProcessingConcurrency();
    private static readonly SemaphoreSlim UploadProcessingSemaphore = new(UploadProcessingConcurrency, UploadProcessingConcurrency);
    private static readonly TimeSpan PresignedBackgroundPersistTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PresignedInitialPersistTimeout = TimeSpan.FromSeconds(6);
    private static readonly ConcurrentDictionary<Guid, DateTime> PresignedCompletionsInFlight = new();
    private static readonly ConcurrentDictionary<Guid, PresignedStatusSnapshot> PresignedStatusSnapshots = new();
    private static readonly TimeSpan PresignedStatusSnapshotTtl = TimeSpan.FromMinutes(30);
    private const long MaxAdminUploadBytes = 100L * 1024 * 1024; // 100 MB
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private sealed class PresignedStatusSnapshot
    {
        public string State { get; set; } = "queued";
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public string? Error { get; set; }
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private static int ResolveUploadProcessingConcurrency()
    {
        var raw = Environment.GetEnvironmentVariable("ADMIN_UPLOAD_PROCESSING_CONCURRENCY");
        if (int.TryParse(raw, out var parsed))
        {
            return Math.Clamp(parsed, 1, 8);
        }

        // Safer default under load: process one admin upload at a time unless explicitly tuned.
        return 1;
    }

    private static void SetPresignedStatus(Guid fileId, string state, string? fileName = null, long? fileSize = null, string? error = null)
    {
        PresignedStatusSnapshots.AddOrUpdate(
            fileId,
            _ => new PresignedStatusSnapshot
            {
                State = state,
                FileName = fileName,
                FileSize = fileSize,
                Error = string.IsNullOrWhiteSpace(error) ? null : error,
                UpdatedAtUtc = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.State = state;
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    existing.FileName = fileName;
                }

                if (fileSize.HasValue)
                {
                    existing.FileSize = fileSize;
                }

                existing.Error = string.IsNullOrWhiteSpace(error) ? existing.Error : error;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                return existing;
            });
    }

    private static bool TryGetRecentPresignedStatus(Guid fileId, out PresignedStatusSnapshot snapshot)
    {
        if (PresignedStatusSnapshots.TryGetValue(fileId, out var existing))
        {
            if ((DateTime.UtcNow - existing.UpdatedAtUtc) <= PresignedStatusSnapshotTtl)
            {
                snapshot = existing;
                return true;
            }

            PresignedStatusSnapshots.TryRemove(fileId, out _);
        }

        snapshot = null!;
        return false;
    }

    private static object BuildPresignedStatusFallbackResponse(Guid id, PresignedStatusSnapshot snapshot)
    {
        var normalized = string.IsNullOrWhiteSpace(snapshot.State)
            ? "processing"
            : snapshot.State.Trim().ToLowerInvariant();

        var isReady = normalized is "ready" or "completed";
        var isFailed = normalized is "failed" or "error";
        var isPending = !isReady && !isFailed;

        return new
        {
            id,
            fileName = snapshot.FileName,
            fileSize = snapshot.FileSize,
            hasStorageKey = isReady,
            isReady,
            pending = isPending,
            failed = isFailed,
            error = isFailed ? (snapshot.Error ?? "Upload failed while processing in background") : null,
            source = "presigned-status-snapshot"
        };
    }

    public AdminController(
        ApplicationDbContext context,
        IMediaStorageService mediaStorageService,
        ICacheService cacheService,


        ILogger<AdminController> logger,
        IRequestMonitoringService monitoringService,
        IGroqTranscriptionLogService groqTranscriptionLog,
        IServiceScopeFactory scopeFactory,
        IHubContext<ResourceHub>? resourceHub = null,
        IOcrBatchService? ocrBatchService = null)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _cacheService = cacheService;
        _logger = logger;
        _monitoringService = monitoringService;
        _groqTranscriptionLog = groqTranscriptionLog;
        _resourceHub = resourceHub;
        _scopeFactory = scopeFactory;
        _ocrBatchService = ocrBatchService ?? new OcrBatchService(new LoggerFactory().CreateLogger<OcrBatchService>());
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

        if (docs.Count == 0)
            return Ok(new { updated = 0, total = 0 });

        // Extract distinct ManagedFileIds
        var mfIds = docs.Select(d => d.ManagedFileId!.Value).Distinct().ToList();

        // Fetch all related ManagedFiles in a single query to avoid N+1 inside the loop
        var mfs = await _context.ManagedFiles
            .Where(m => mfIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        int updated = 0;
        foreach (var doc in docs)
        {
            if (doc.ManagedFileId is not Guid mfId) continue;
            if (!mfs.TryGetValue(mfId, out var mf)) continue;

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
        bool bypassCache = HttpContext.Request.Query.ContainsKey("cb") 
                        || HttpContext.Request.Query.ContainsKey("v") 
                        || HttpContext.Request.Query.ContainsKey("nocache")
                        || HttpContext.Request.Query.ContainsKey("_");

        try
        {
            if (!bypassCache)
            {
                // Try to get from cache first
                var cachedResult = await _cacheService.GetAsync<object>(cacheKey);
                if (cachedResult != null)
                {
                    return Ok(cachedResult);
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeoutCts.CancelAfter(InitEndpointTimeout);
            var ct = timeoutCts.Token;

            var visibleFilesQuery = _context.ManagedFiles
                .AsNoTracking()
                .Where(f => !string.IsNullOrWhiteSpace(f.StorageKey)
                    && !EF.Functions.Like(f.StorageKey, "FAILED:%")
                    && f.FileSize > 0);

            var totalCount = await visibleFilesQuery.CountAsync(ct);
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Fetch files without the Groups navigation property to avoid a correlated
            // subquery (N+1) that issues one extra SQL round-trip per file row.
            var fileList = await visibleFilesQuery
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
                    f.FileSize
                })
                .ToListAsync(ct);

            // Bulk-fetch groups for the returned file IDs in a single query.
            var fileIds = fileList.Select(f => f.Id).ToList();
            var groupRowsForFiles = fileIds.Count == 0
                ? new List<(Guid FileId, Guid GroupId, string GroupName)>()
                : await _context.Set<ManagedFileGroup>()
                    .AsNoTracking()
                    .Where(fg => fileIds.Contains(fg.ManagedFileId))
                    .Select(fg => new
                    {
                        fg.ManagedFileId,
                        fg.ResourceGroup.Id,
                        fg.ResourceGroup.Name
                    })
                    .ToListAsync(ct)
                    .ContinueWith(t => t.Result
                        .Select(x => (FileId: x.ManagedFileId, GroupId: x.Id, GroupName: x.Name))
                        .ToList(), TaskContinuationOptions.ExecuteSynchronously);

            var groupsByFileId = groupRowsForFiles
                .GroupBy(r => r.FileId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var files = fileList.Select(f => new
            {
                f.Id,
                f.FileName,
                f.Label,
                f.ContentType,
                f.StorageKey,
                f.UploadedAt,
                f.FileSize,
                Groups = groupsByFileId.TryGetValue(f.Id, out var grps)
                    ? grps.Select(g => new { Id = g.GroupId, Name = g.GroupName }).ToList<object>()
                    : new List<object>()
            }).ToList();

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
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out retrieving managed files for page {Page} size {PageSize}", page, pageSize);
            return Ok(CreateEmptyPagedResult(page, pageSize));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving managed files for page {Page} size {PageSize}", page, pageSize);
            return Ok(CreateEmptyPagedResult(page, pageSize));
        }
    }

    /// <summary>
    /// Returns a presigned S3 PUT URL so the browser can upload directly to S3, bypassing
    /// the CloudFront 60-second origin_read_timeout.  Returns 501 when not available (LocalStack/MinIO).
    /// </summary>

    [HttpGet("files/upload-url")]
    public async Task<IActionResult> GetPresignedUploadUrl([FromQuery] string filename, [FromQuery] string contentType = "application/octet-stream")
    {
        if (string.IsNullOrWhiteSpace(filename))
            return BadRequest(new { error = "filename is required" });

        var safeFilename = Path.GetFileName(filename);
        if (string.IsNullOrWhiteSpace(safeFilename))
            return BadRequest(new { error = "Invalid filename" });

        var result = await _mediaStorageService.GetPresignedUploadUrlAsync(safeFilename, contentType, TimeSpan.FromMinutes(15));
        if (result == null)
            return StatusCode(StatusCodes.Status501NotImplemented, new { error = "Presigned upload not available in this environment" });

        return Ok(new { presignedUrl = result.Value.PresignedUrl, key = result.Value.Key, expiresInSeconds = 900 });
    }

    /// <summary>
    /// Called by the browser after a successful direct-to-S3 presigned upload.
    /// Creates a DB placeholder and starts RAG processing in the background.
    /// </summary>

    [HttpPost("files/complete")]
    public async Task<IActionResult> CompletePresignedUpload([FromBody] CompleteUploadRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required" });

        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Contains("..") || request.Key.StartsWith('/'))
            return BadRequest(new { error = "Invalid key" });
        if (string.IsNullOrWhiteSpace(request.FileName))
            return BadRequest(new { error = "FileName is required" });

        var requestId = Guid.NewGuid().ToString("N");
        var fileId = request.FileId ?? Guid.NewGuid();
        var inFlightKey = fileId;
        var label = string.IsNullOrWhiteSpace(request.Label) ? request.FileName : request.Label;
        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType;
        var safeFileSize = Math.Max(0, request.FileSize);
        SetPresignedStatus(fileId, "queued", request.FileName, safeFileSize);

        // Persist an immediate placeholder so /status can observe this upload before the
        // background worker starts processing.
        try
        {
            using var initPersistCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            initPersistCts.CancelAfter(PresignedInitialPersistTimeout);
            await EnsurePresignedPlaceholderAsync(fileId, request.FileName, contentType, label, safeFileSize, initPersistCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out creating presigned placeholder for {FileId}; background processing will continue", fileId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to persist presigned placeholder for {FileId}; background processing will continue", fileId);
        }

        // Idempotency guard: retries for the same client-generated fileId should not spawn duplicate work.
        // Evict stale in-flight entries to avoid rare stuck-key scenarios after worker interruptions.
        if (PresignedCompletionsInFlight.TryGetValue(inFlightKey, out var startedAt)
            && (DateTime.UtcNow - startedAt) > TimeSpan.FromMinutes(10))
        {
            PresignedCompletionsInFlight.TryRemove(inFlightKey, out _);
        }

        if (!PresignedCompletionsInFlight.TryAdd(inFlightKey, DateTime.UtcNow))
        {
            SetPresignedStatus(fileId, "processing", request.FileName, safeFileSize);
            return Accepted(new
            {
                id = fileId,
                requestId,
                status = "processing",
                registration = "in-progress"
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessPresignedUploadAsync(fileId, request.Key, request.FileName, contentType, label, safeFileSize, requestId);
            }
            finally
            {
                PresignedCompletionsInFlight.TryRemove(inFlightKey, out _);
                if (fileId != inFlightKey)
                {
                    PresignedCompletionsInFlight.TryRemove(fileId, out _);
                }
            }
        });

        return Accepted(new
        {
            id = fileId,
            requestId,
            status = "processing",
            registration = "queued"
        });
    }

    private async Task EnsurePresignedPlaceholderAsync(
        Guid fileId,
        string fileName,
        string contentType,
        string label,
        long fileSize,
        CancellationToken ct)
    {
        var managedFile = await _context.ManagedFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (managedFile == null)
        {
            _context.ManagedFiles.Add(new ManagedFile
            {
                Id = fileId,
                FileName = fileName,
                Label = label,
                ContentType = contentType,
                StorageKey = "PENDING: presigned upload registered",
                UploadedAt = DateTime.UtcNow,
                FileSize = fileSize
            });

            await _context.SaveChangesAsync(ct);
            return;
        }

        var changed = false;
        if (!string.Equals(managedFile.FileName, fileName, StringComparison.Ordinal)) { managedFile.FileName = fileName; changed = true; }
        if (!string.Equals(managedFile.Label, label, StringComparison.Ordinal)) { managedFile.Label = label; changed = true; }
        if (!string.Equals(managedFile.ContentType, contentType, StringComparison.Ordinal)) { managedFile.ContentType = contentType; changed = true; }
        if (managedFile.FileSize != fileSize) { managedFile.FileSize = fileSize; changed = true; }
        if (string.IsNullOrWhiteSpace(managedFile.StorageKey)
            || managedFile.StorageKey.StartsWith("FAILED:", StringComparison.OrdinalIgnoreCase)
            || managedFile.StorageKey.StartsWith("PENDING:", StringComparison.OrdinalIgnoreCase))
        {
            managedFile.StorageKey = "PENDING: presigned upload registered";
            changed = true;
        }

        if (changed)
        {
            _context.ManagedFiles.Update(managedFile);
            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessPresignedUploadAsync(
        Guid fileId,
        string s3Key,
        string fileName,
        string contentType,
        string label,
        long fileSize,
        string requestId)
    {
        await UploadProcessingSemaphore.WaitAsync();
        try
        {
            SetPresignedStatus(fileId, "processing", fileName, fileSize);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            using var dbTimeoutCts = new CancellationTokenSource(PresignedBackgroundPersistTimeout);
            var dbCt = dbTimeoutCts.Token;

            var managedFile = await db.ManagedFiles.FirstOrDefaultAsync(f => f.Id == fileId, dbCt);
            if (managedFile == null)
            {
                managedFile = new ManagedFile
                {
                    Id = fileId,
                    FileName = fileName,
                    Label = label,
                    ContentType = contentType,
                    StorageKey = s3Key,
                    UploadedAt = DateTime.UtcNow,
                    FileSize = fileSize
                };
                db.ManagedFiles.Add(managedFile);
                await db.SaveChangesAsync(dbCt);
            }
            else
            {
                var changed = false;
                if (!string.Equals(managedFile.FileName, fileName, StringComparison.Ordinal)) { managedFile.FileName = fileName; changed = true; }
                if (!string.Equals(managedFile.Label, label, StringComparison.Ordinal)) { managedFile.Label = label; changed = true; }
                if (!string.Equals(managedFile.ContentType, contentType, StringComparison.Ordinal)) { managedFile.ContentType = contentType; changed = true; }
                if (!string.Equals(managedFile.StorageKey, s3Key, StringComparison.Ordinal)) { managedFile.StorageKey = s3Key; changed = true; }
                if (managedFile.FileSize != fileSize) { managedFile.FileSize = fileSize; changed = true; }

                if (changed)
                {
                    db.ManagedFiles.Update(managedFile);
                    await db.SaveChangesAsync(dbCt);
                }
            }

            SetPresignedStatus(fileId, "ready", fileName, fileSize);

            try { await InvalidateFilesCache(); } catch { /* best-effort */ }
            try { await NotifyFilesChangedAsync(); } catch { /* best-effort */ }

            try
            {
                _logger.LogInformation("Presigned upload file {FileId} processed for RAG", fileId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG processing failed for presigned upload {FileId}, file is still accessible", fileId);
            }

            try { await InvalidateFilesCache(); } catch { /* best-effort */ }
            try { await NotifyFilesChangedAsync(); } catch { /* best-effort */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessPresignedUploadAsync failed for {FileId}", fileId);
            SetPresignedStatus(fileId, "failed", fileName, fileSize, ex.Message);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var mf = await db.ManagedFiles.FirstOrDefaultAsync(f => f.Id == fileId);
                if (mf != null)
                {
                    mf.StorageKey = $"FAILED: {ex.Message}";
                    db.ManagedFiles.Update(mf);
                    await db.SaveChangesAsync();
                }
            }
            catch { /* best-effort */ }
        }
        finally
        {
            UploadProcessingSemaphore.Release();
        }
    }

    /// <summary>
    /// Multi-page OCR batch capture endpoint. Preprocesses captured images, runs Tesseract OCR,
    /// compiles into a searchable multi-page PDF, stores it in the repository, and returns the file ID.
    /// </summary>
    [HttpPost("files/ocr-batch")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(MaxAdminUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAdminUploadBytes, ValueLengthLimit = int.MaxValue)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ProcessOcrBatch([FromForm] List<IFormFile> files, [FromForm] string? label)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "At least one page image is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var documentLabel = string.IsNullOrWhiteSpace(label)
            ? $"Document Scan - {DateTime.Now:yyyy-MM-dd HH:mm}"
            : label.Trim();

        _logger.LogInformation("Starting OCR batch processing for '{Label}' with {Count} page(s)", documentLabel, files.Count);

        var pageDataList = new List<PageOcrData>();
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var imageBytes = ms.ToArray();

            var pageOcr = await _ocrBatchService.ProcessSinglePageAsync(imageBytes, i + 1);
            pageDataList.Add(pageOcr);
        }

        // Generate searchable multi-page PDF with selectable text layer
        var pdfBytes = await _ocrBatchService.GenerateSearchablePdfAsync(pageDataList);
        var fileId = Guid.NewGuid();
        var fileName = $"scan_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
        const string contentType = "application/pdf";

        using var pdfStream = new MemoryStream(pdfBytes);
        var uploadMeta = S3FileIndexService.BuildUploadMetadata(fileId, fileName, documentLabel);
        var storageKey = await _mediaStorageService.UploadMediaAsync(pdfStream, fileName, contentType, uploadMeta);

        using var hashStream = new MemoryStream(pdfBytes, writable: false);
        var contentHash = _mediaStorageService.ComputeContentHash(hashStream);

        var managedFile = new ManagedFile
        {
            Id = fileId,
            FileName = fileName,
            Label = documentLabel,
            ContentType = contentType,
            StorageKey = storageKey,
            ContentHash = contentHash,
            UploadedAt = DateTime.UtcNow,
            FileSize = pdfBytes.LongLength
        };

        _context.ManagedFiles.Add(managedFile);
        await _context.SaveChangesAsync();

        try { await InvalidateFilesCache(); } catch { /* best-effort */ }
        try { await NotifyFilesChangedAsync(); } catch { /* best-effort */ }

        _logger.LogInformation("OCR batch processing complete: '{Label}' saved as {FileId} ({PageCount} pages, {Bytes} bytes)",
            documentLabel, fileId, files.Count, pdfBytes.Length);

        return Ok(new
        {
            id = fileId,
            name = documentLabel,
            fileName = fileName,
            totalPages = files.Count,
            size = pdfBytes.Length,
            success = true
        });
    }

    /// <summary>
    /// Upload a new file with label - queues S3 upload and RAG processing to background to avoid request timeouts
    /// </summary>

    [HttpPost("files")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(MaxAdminUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAdminUploadBytes, ValueLengthLimit = int.MaxValue)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UploadFile([FromForm] FileUploadRequest request)
    {
        if (request?.File == null || request.File.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "File is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (request.File.Length > MaxAdminUploadBytes)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = $"File size exceeds maximum of {MaxAdminUploadBytes / (1024 * 1024)}MB",
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

        var requestId = Guid.NewGuid().ToString("N");
        try
        {
            // Keep the request path minimal to avoid gateway timeouts.
            byte[] uploadedBytes;
            using (var ms = new MemoryStream())
            {
                await request.File.CopyToAsync(ms);
                uploadedBytes = ms.ToArray();
            }

            _logger.LogInformation("File upload received and queued: {FileName}, {Size} bytes, Label: {Label}", 
                request.File.FileName, uploadedBytes.LongLength, request.Label);

            var fileId = Guid.NewGuid();
            var contentType = request.File.ContentType ?? "application/octet-stream";
            var label = string.IsNullOrWhiteSpace(request.Label) ? (request.File.FileName ?? "Untitled") : request.Label;

            // Persist a lightweight placeholder immediately so status polling has a row to query.
            _context.ManagedFiles.Add(new ManagedFile
            {
                Id = fileId,
                FileName = request.File.FileName ?? "Untitled",
                Label = label,
                ContentType = contentType,
                StorageKey = "PENDING: upload queued",
                UploadedAt = DateTime.UtcNow,
                FileSize = 0
            });
            await _context.SaveChangesAsync();

            // Queue S3 upload and RAG processing to background task (keep request fast)
            _ = Task.Run(() => ProcessQueuedFileUploadAsync(fileId, uploadedBytes, request.File.FileName ?? string.Empty, 
                contentType, label, requestId));

            return Accepted(new { id = fileId, requestId, status = "processing" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file");
            try { await _monitoringService.CompleteRequest(requestId, false, error: ex.Message); } catch (Exception monitorEx) { _logger.LogWarning(monitorEx, "Failed to complete monitoring request {RequestId}", requestId); }
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while uploading the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    private async Task ProcessQueuedFileUploadAsync(
        Guid fileId,
        byte[] uploadedBytes,
        string fileName,
        string contentType,
        string label,
        string requestId)
    {
        await UploadProcessingSemaphore.WaitAsync();
        try
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediaStorageService = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var monitoringService = scope.ServiceProvider.GetService<IRequestMonitoringService>();

                if (monitoringService != null)
                {
                    try
                    {
                        await monitoringService.StartRequest("FileUpload", $"Uploading: {fileName}", requestId);
                    }
                    catch (Exception monitorStartEx)
                    {
                        _logger.LogWarning(monitorStartEx, "Failed to initialize monitoring for queued file upload {FileId}", fileId);
                    }
                }

                if (monitoringService != null)
                {
                    await monitoringService.UpdateRequestStatus(requestId, "Uploading", "Uploading file to storage...");
                }

                // Upload to storage — include metadata so S3FileIndexService can reconstruct the
                // ManagedFile record on next boot without a separate database save.
                using var stream = new MemoryStream(uploadedBytes);
                var uploadMeta = S3FileIndexService.BuildUploadMetadata(fileId, fileName, label);
                var storageKey = await mediaStorageService.UploadMediaAsync(stream, fileName, contentType, uploadMeta);

                // Update existing record if present, otherwise create it here.
                var managedFile = await db.ManagedFiles.FirstOrDefaultAsync(f => f.Id == fileId);
                string? contentHash = null;
                try
                {
                    using var hashStream = new MemoryStream(uploadedBytes, writable: false);
                    contentHash = mediaStorageService.ComputeContentHash(hashStream);
                }
                catch (Exception hashEx)
                {
                    _logger.LogWarning(hashEx, "Failed to compute content hash for queued file upload {FileId}", fileId);
                }

                if (managedFile == null)
                {
                    managedFile = new ManagedFile
                    {
                        Id = fileId,
                        FileName = fileName,
                        Label = label,
                        ContentType = contentType,
                        StorageKey = storageKey,
                        ContentHash = contentHash,
                        UploadedAt = DateTime.UtcNow,
                        FileSize = uploadedBytes.LongLength
                    };
                    db.ManagedFiles.Add(managedFile);
                }
                else
                {
                    managedFile.FileName = fileName;
                    managedFile.Label = label;
                    managedFile.ContentType = contentType;
                    managedFile.StorageKey = storageKey;
                    managedFile.ContentHash = contentHash;
                    managedFile.FileSize = uploadedBytes.LongLength;
                    db.ManagedFiles.Update(managedFile);
                }

                await db.SaveChangesAsync();

                // Make the uploaded file visible in the recent files list as soon as it is stored.
                try { await InvalidateFilesCache(); } catch { /* best-effort */ }
                try { await NotifyFilesChangedAsync(); } catch { /* best-effort */ }

                if (monitoringService != null)
                {
                    await monitoringService.UpdateRequestStatus(requestId, "Processing", "Processing file for RAG indexing...");
                }

                // Process file for RAG indexing
                try
                {
                    _logger.LogInformation("File {FileId} processed for RAG", fileId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process file {FileId} for RAG, but file was uploaded successfully", fileId);
                }

                // Invalidate cache and notify listeners so UI reflects the completed upload
                try { await InvalidateFilesCache(); } catch { /* best-effort */ }
                try { await NotifyFilesChangedAsync(); } catch { /* best-effort */ }

                if (monitoringService != null)
                {
                    await monitoringService.CompleteRequest(requestId, true, $"File {fileName} uploaded and processed successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queued file upload failed for {FileId}", fileId);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var mf = await db.ManagedFiles.FirstOrDefaultAsync(f => f.Id == fileId);
                    if (mf != null)
                    {
                        mf.StorageKey = $"FAILED: {ex.Message}";
                        db.ManagedFiles.Update(mf);
                        await db.SaveChangesAsync();
                    }
                    else
                    {
                        db.ManagedFiles.Add(new ManagedFile
                        {
                            Id = fileId,
                            FileName = fileName,
                            Label = label,
                            ContentType = contentType,
                            StorageKey = $"FAILED: {ex.Message}",
                            UploadedAt = DateTime.UtcNow,
                            FileSize = uploadedBytes.LongLength
                        });
                        await db.SaveChangesAsync();
                    }

                    var monitoringService = scope.ServiceProvider.GetService<IRequestMonitoringService>();
                    if (monitoringService != null)
                    {
                        await monitoringService.CompleteRequest(requestId, false, error: ex.Message);
                    }
                }
                catch { /* best-effort cleanup */ }

                try { await InvalidateFilesCache(); } catch { }
                try { await NotifyFilesChangedAsync(); } catch { }
            }
        }
        finally
        {
            UploadProcessingSemaphore.Release();
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

        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.StartsWith("PENDING:", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status409Conflict, new ProblemDetails
            {
                Title = "File not ready",
                Detail = "The selected file is still processing. Try again in a few seconds.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (storageKey.StartsWith("FAILED:", StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "File unavailable",
                Detail = storageKey,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }

        try
        {
            // DownloadFileAsync tries _mediaBucket first, then _documentsBucket as fallback.
            // This covers both MediaController uploads (media/) and RAG-processed docs (documents/).
            var stream = await _mediaStorageService.DownloadFileAsync(storageKey);
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

            // Return the stream directly to support range requests (PDF viewing often requires this)
            // inline disposition = browser displays (not downloads); filename = PDF viewer tab title.
            var safeFileName = (fileName ?? "document").Replace("\"", "");
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{safeFileName}\"";
            
            // Allow range processing if the stream supports seeking
            if (stream.CanSeek)
            {
                return File(stream, contentType, enableRangeProcessing: true);
            }
            
            return File(stream, contentType);
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
            // DownloadFileAsync tries _mediaBucket first, then _documentsBucket as fallback.
            var stream = await _mediaStorageService.DownloadFileAsync(storageKey);
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

            // Force download with filename support for range processing if stream allows
            if (stream.CanSeek)
            {
                return File(stream, contentType, fileName, enableRangeProcessing: true);
            }
            
            return File(stream, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileId}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Download all abbreviations from the database as CSV.
    /// </summary>


    /// <returns>A file containing the abbreviations in CSV format.</returns>
    [HttpGet("abbreviations/download")]
    public IActionResult DownloadAbbreviationsCsv([FromServices] IAbbreviationService abbreviationService)
    {
        var abbreviations = abbreviationService.GetAllCachedAbbreviations()
            .OrderBy(a => a.ShortForm)
            .ThenBy(a => a.ExpandedForm)
            .ToList();

        static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Id,ShortForm,ExpandedForm,Category,Source,CreatedAt,UpdatedAt");

        foreach (var a in abbreviations)
        {
            sb.Append(EscapeCsv(a.Id.ToString()));
            sb.Append(',');
            sb.Append(EscapeCsv(a.ShortForm));
            sb.Append(',');
            sb.Append(EscapeCsv(a.ExpandedForm));
            sb.Append(',');
            sb.Append(EscapeCsv(a.Category));
            sb.Append(',');
            sb.Append(EscapeCsv(a.Source));
            sb.Append(',');
            sb.Append(EscapeCsv(a.CreatedAt.ToString("o")));
            sb.Append(',');
            sb.Append(EscapeCsv(a.UpdatedAt.ToString("o")));
            sb.AppendLine();
        }

        var fileName = $"abbreviations-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        var csvBytes = Encoding.UTF8.GetBytes(sb.ToString());

        return File(csvBytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>
    /// Debug: returns size, first 16 bytes (hex), stored content-type and storage key for a file.
    /// </summary>

    [HttpGet("files/{id}/status")]
    public async Task<IActionResult> FileStatus(Guid id)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            var ct = timeoutCts.Token;

            var file = await _context.ManagedFiles
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, ct);

            if (file != null)
            {
                var failed = !string.IsNullOrWhiteSpace(file.StorageKey) && file.StorageKey.StartsWith("FAILED:", StringComparison.OrdinalIgnoreCase);
                var pending = !failed && !string.IsNullOrWhiteSpace(file.StorageKey) && file.StorageKey.StartsWith("PENDING:", StringComparison.OrdinalIgnoreCase);
                var ready = !failed && !pending && !string.IsNullOrWhiteSpace(file.StorageKey) && file.FileSize > 0;

                if (ready || failed)
                {
                    PresignedStatusSnapshots.TryRemove(file.Id, out _);
                }

                return Ok(new
                {
                    id = file.Id,
                    fileName = file.FileName,
                    label = file.Label,
                    contentType = file.ContentType,
                    fileSize = file.FileSize,
                    uploadedAt = file.UploadedAt,
                    hasStorageKey = !string.IsNullOrWhiteSpace(file.StorageKey),
                    isReady = ready,
                    pending,
                    failed,
                    error = failed ? file.StorageKey : null
                });
            }

            var doc = await _context.DocumentMetadatas
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (doc == null)
            {
                return NotFound(new { error = "Not found in ManagedFiles or DocumentMetadatas", id });
            }

            return Ok(new
            {
                id = doc.Id,
                fileName = doc.FileName,
                contentType = doc.ContentType,
                fileSize = doc.FileSize,
                uploadedAt = doc.UploadedAt,
                hasStorageKey = !string.IsNullOrWhiteSpace(doc.StorageKey),
                isReady = !string.IsNullOrWhiteSpace(doc.StorageKey) && doc.FileSize > 0,
                pending = false,
                failed = !string.IsNullOrWhiteSpace(doc.ProcessingError),
                error = doc.ProcessingError,
                managedFileId = doc.ManagedFileId,
                isProcessed = doc.IsProcessed,
                processedAt = doc.ProcessedAt
            });
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out retrieving file status for {FileId}", id);

            if (TryGetRecentPresignedStatus(id, out var snapshot))
            {
                return Ok(BuildPresignedStatusFallbackResponse(id, snapshot));
            }

            return Ok(new
            {
                id,
                isReady = false,
                pending = true,
                failed = false,
                error = "Status check timed out while the server is busy"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed retrieving file status for {FileId}", id);

            if (TryGetRecentPresignedStatus(id, out var snapshot))
            {
                return Ok(BuildPresignedStatusFallbackResponse(id, snapshot));
            }

            return Ok(new
            {
                id,
                isReady = false,
                pending = true,
                failed = false,
                error = "Status check failed while the server is busy"
            });
        }
    }

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

        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.StartsWith("PENDING:", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status409Conflict, new { error = "File is still processing", id, status = "pending" });
        }

        if (storageKey.StartsWith("FAILED:", StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new { error = "File upload failed", details = storageKey, id, status = "failed" });
        }

        try
        {
            var stream = await _mediaStorageService.DownloadFileAsync(storageKey);
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


    /// <returns>A status confirming the reindex.</returns>
    [HttpPost("reindex")]
    public async Task<ActionResult> ReindexAll([FromQuery] bool force = false)
    {
        string? requestId = null;
        try
        {
            requestId = await _monitoringService.StartRequest("DocumentReindex", 
                $"Reindexing all documents (force: {force})");
            await _monitoringService.UpdateRequestStatus(requestId, "Reindexing documents...");
            
            int reindexed = 0;
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



    #region Helper Methods

    private sealed class GroupFileRow
    {
        public Guid ResourceGroupId { get; set; }
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    private sealed class GroupPromptRow
    {
        public Guid ResourceGroupId { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class GroupDocumentRow
    {
        public Guid ResourceGroupId { get; set; }
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }

    private sealed class GroupConversationRow
    {
        public Guid ResourceGroupId { get; set; }
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    private static object CreateEmptyPagedResult(int page, int pageSize)
    {
        return new
        {
            Data = Array.Empty<object>(),
            Degraded = true,
            Error = "temporary_unavailable",
            RetryAfterSeconds = 30,
            Pagination = new
            {
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = 0,
                TotalPages = 0,
                HasPrevious = page > 1,
                HasNext = false
            }
        };
    }

    private Task NotifyFilesChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("filesChanged")
            : Task.CompletedTask;
    }

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

    private async Task InvalidateAllCaches()
    {
        await InvalidateFilesCache();
    }

    #endregion

    #region Batch Operations

    /// <summary>
    /// Get files, prompts, and groups in a single batch request for efficiency
    /// </summary>

    [HttpGet("batch")]
    public async Task<ActionResult<object>> GetBatch([FromQuery] bool includeFiles = true, [FromQuery] int maxItems = 1000)
    {
        try
        {
            var result = new Dictionary<string, object>();

            if (includeFiles)
            {
                var files = await _context.ManagedFiles
                    .Include(f => f.FileGroups)
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
                        f.FileSize
                    })
                    .ToListAsync();

                result["files"] = new
                {
                    data = files,
                    count = files.Count
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
    /// Return recent Groq transcription entries (wakeword + command) for the live viewer.
    /// </summary>
    [HttpGet("groq-transcriptions")]
    public ActionResult<object> GetGroqTranscriptions([FromQuery] int maxCount = 200)
    {
        var entries = _groqTranscriptionLog.GetRecent(Math.Clamp(maxCount, 1, 500));
        return Ok(new { entries, count = entries.Count });
    }

    /// <summary>
    /// Clear all stored Groq transcription log entries.
    /// </summary>
    [HttpDelete("groq-transcriptions")]
    public IActionResult ClearGroqTranscriptions()
    {
        _groqTranscriptionLog.Clear();
        return Ok(new { cleared = true });
    }

    /// <summary>
    /// Clear ALL history data: request monitoring, files, documents, conversations, AI logs, and transcription jobs.
    /// Nothing is preserved — all user data is deleted.
    /// </summary>


    /// <returns>A dictionary of deleted record counts.</returns>
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

            // Delete managed files
            counts["managedFiles"] = await _context.ManagedFiles.ExecuteDeleteAsync();

            // Delete AI interaction logs and transcription jobs
            counts["aiInteractionLogs"] = await _context.AIInteractionLogs.ExecuteDeleteAsync();
            counts["transcriptionJobs"] = await _context.TranscriptionJobs.ExecuteDeleteAsync();

            // Delete all prompt templates (ManagedPromptGroup join rows cascade at DB level)
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
                Task.CompletedTask
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
