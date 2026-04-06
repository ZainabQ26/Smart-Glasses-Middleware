using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Hosted service that scans the S3/MinIO bucket on startup and rebuilds the
/// in-memory <see cref="ManagedFile"/> index from what is actually stored there.
///
/// This eliminates the need to persist file metadata separately: S3 is the source
/// of truth. Every file upload stores a small amount of user-defined metadata
/// (sgm-file-id, sgm-filename, sgm-label) alongside the object. On restart those
/// values are read back and used to reconstruct the in-memory database records.
/// Files that pre-date this feature (no sgm-* metadata) are still indexed using
/// the information that can be inferred from the S3 key and object properties.
/// </summary>
public class S3FileIndexService : IHostedService
{
    // S3 user-metadata keys used when uploading. Must be lowercase for AWS SDK.
    public const string MetaFileId = "sgm-file-id";
    public const string MetaFileName = "sgm-filename";
    public const string MetaLabel = "sgm-label";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<S3FileIndexService> _logger;
    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;

    public S3FileIndexService(IServiceScopeFactory scopeFactory, ILogger<S3FileIndexService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("S3FileIndexService: scheduling ManagedFile index rebuild from S3 in background");

        _backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundTask = Task.Run(() => RebuildIndexAsync(_backgroundCts.Token), CancellationToken.None);

        // Never block application startup on S3 listing/index rebuild.
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_backgroundCts == null || _backgroundTask == null)
        {
            return;
        }

        try
        {
            _backgroundCts.Cancel();
            await _backgroundTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("S3FileIndexService: background indexing stopped during shutdown");
        }
        finally
        {
            _backgroundCts.Dispose();
            _backgroundCts = null;
            _backgroundTask = null;
        }
    }

    /// <summary>
    /// Builds the metadata dictionary that should be attached to every S3 upload so
    /// the file can be fully reconstructed on the next startup without a separate database.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildUploadMetadata(Guid fileId, string fileName, string label)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetaFileId] = fileId.ToString(),
            [MetaFileName] = fileName,
            [MetaLabel] = label
        };

    private async Task RebuildIndexAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("S3FileIndexService: rebuilding ManagedFile index from S3");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            IReadOnlyList<StoredFileInfo> files;
            try
            {
                files = await storageService.ListFilesAsync(cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "S3FileIndexService: could not list files from S3 (may be unconfigured in dev); skipping index rebuild");
                return;
            }

            _logger.LogInformation("S3FileIndexService: found {Count} objects in S3", files.Count);

            int added = 0, skipped = 0;
            foreach (var file in files)
            {
                // Skip internal/system keys (e.g. state-persistence snapshots)
                if (file.Key.StartsWith("_state/", StringComparison.OrdinalIgnoreCase) ||
                    file.Key.StartsWith("_index/", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                // Derive the Guid ID from stored metadata or from the S3 key path
                Guid fileId = ExtractFileId(file);

                // Skip if already indexed (idempotent)
                if (await db.ManagedFiles.AnyAsync(f => f.Id == fileId || f.StorageKey == file.Key, cts.Token))
                {
                    skipped++;
                    continue;
                }

                var label = file.UserMetadata.TryGetValue(MetaLabel, out var lbl) && !string.IsNullOrWhiteSpace(lbl)
                    ? lbl
                    : file.FileName;

                db.ManagedFiles.Add(new ManagedFile
                {
                    Id = fileId,
                    FileName = file.FileName,
                    Label = label,
                    ContentType = file.ContentType,
                    StorageKey = file.Key,
                    UploadedAt = file.LastModified,
                    FileSize = file.Size
                });
                added++;
            }

            if (added > 0)
            {
                await db.SaveChangesAsync(cts.Token);
                _logger.LogInformation("S3FileIndexService: indexed {Added} new files from S3 ({Skipped} already known)", added, skipped);
            }
            else
            {
                _logger.LogInformation("S3FileIndexService: all {Count} S3 objects already indexed ({Skipped} skipped)", files.Count, skipped);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("S3FileIndexService: startup indexing timed out or was cancelled; file list may be incomplete until next run");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3FileIndexService: unexpected error during startup indexing");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Guid ExtractFileId(StoredFileInfo file)
    {
        // 1. Explicit metadata field
        if (file.UserMetadata.TryGetValue(MetaFileId, out var raw) &&
            Guid.TryParse(raw, out var fromMeta))
        {
            return fromMeta;
        }

        // 2. Key format: "{guid}/{filename}" (media uploads)
        var firstSegment = file.Key.Split('/')[0];
        if (Guid.TryParse(firstSegment, out var fromKey))
        {
            return fromKey;
        }

        // 3. Key format: "uploads/{guid}.ext" (document uploads)
        var noExt = System.IO.Path.GetFileNameWithoutExtension(file.Key.Split('/').Last());
        if (Guid.TryParse(noExt, out var fromDocKey))
        {
            return fromDocKey;
        }

        // 4. Deterministic fallback — hash the key so restarts produce the same ID
        return GuidFromString(file.Key);
    }

    private static Guid GuidFromString(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        // Take the first 16 bytes of the SHA-256 hash as a deterministic Guid
        return new Guid(hash[..16]);
    }
}
