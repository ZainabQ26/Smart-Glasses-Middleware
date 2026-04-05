using Minio;
using Minio.DataModel.Args;
using System.Security.Cryptography;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Metadata for a single file stored in S3/MinIO, returned by <see cref="IMediaStorageService.ListFilesAsync"/>.
/// </summary>
public record StoredFileInfo(
    string Key,
    string FileName,
    string ContentType,
    long Size,
    DateTime LastModified,
    IReadOnlyDictionary<string, string> UserMetadata);

public interface IMediaStorageService
{
    Task<string> UploadMediaAsync(Stream fileStream, string fileName, string contentType, IReadOnlyDictionary<string, string>? metadata = null);
    Task<Stream> DownloadMediaAsync(string key);
    Task DeleteMediaAsync(string key);
    string ComputeContentHash(Stream fileStream);
    Task<string> UploadFileAsync(string key, Stream fileStream, string contentType, IReadOnlyDictionary<string, string>? metadata = null);
    Task<Stream> DownloadFileAsync(string key);
    /// <summary>
    /// Generate a presigned URL the browser can PUT a file to directly (bypasses CloudFront timeout).
    /// Returns null if not supported in the current environment (e.g. LocalStack/Minio).
    /// </summary>
    Task<(string PresignedUrl, string Key)?> GetPresignedUploadUrlAsync(string fileName, string contentType, TimeSpan expiry);
    /// <summary>
    /// List all files stored in the bucket, returning their key, size, content-type and
    /// any user-defined metadata set at upload time (e.g. <c>sgm-filename</c>, <c>sgm-label</c>).
    /// </summary>
    Task<IReadOnlyList<StoredFileInfo>> ListFilesAsync(CancellationToken ct = default);
}

public class MinioMediaStorageService : IMediaStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly ILogger<MinioMediaStorageService> _logger;

    public MinioMediaStorageService(IMinioClient minioClient, IConfiguration configuration, ILogger<MinioMediaStorageService> logger)
    {
        _minioClient = minioClient;
        _bucketName = configuration["MinIO:BucketName"] ?? "smart-glasses-media";
        _logger = logger;
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName, string contentType, IReadOnlyDictionary<string, string>? metadata = null)
    {
        try
        {
            // Ensure bucket exists
            var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucketName);
            bool found = await _minioClient.BucketExistsAsync(bucketExistsArgs);

            if (!found)
            {
                var makeBucketArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _minioClient.MakeBucketAsync(makeBucketArgs);
            }

            // Generate unique key
            var key = $"{Guid.NewGuid()}/{fileName}";

            // Upload file
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(key)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType);

            if (metadata != null && metadata.Count > 0)
            {
                putObjectArgs = putObjectArgs.WithHeaders(metadata.ToDictionary(kv => kv.Key, kv => kv.Value));
            }

            await _minioClient.PutObjectAsync(putObjectArgs);

            _logger.LogInformation("Uploaded media to MinIO: {Key}", key);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading media to MinIO");
            throw;
        }
    }

    public async Task<Stream> DownloadMediaAsync(string key)
    {
        try
        {
            var memoryStream = new MemoryStream();
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(key)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;

            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading media from MinIO: {Key}", key);
            throw;
        }
    }

    public async Task DeleteMediaAsync(string key)
    {
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(key);

            await _minioClient.RemoveObjectAsync(removeObjectArgs);

            _logger.LogInformation("Deleted media from MinIO: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting media from MinIO: {Key}", key);
            throw;
        }
    }

    public string ComputeContentHash(Stream fileStream)
    {
        using var sha256 = SHA256.Create();
        var originalPosition = fileStream.Position;
        fileStream.Position = 0;

        var hash = sha256.ComputeHash(fileStream);
        fileStream.Position = originalPosition;

        return Convert.ToHexString(hash);
    }

    public async Task<string> UploadFileAsync(string key, Stream fileStream, string contentType, IReadOnlyDictionary<string, string>? metadata = null)
    {
        try
        {
            // Ensure bucket exists
            var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucketName);
            bool found = await _minioClient.BucketExistsAsync(bucketExistsArgs);

            if (!found)
            {
                var makeBucketArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _minioClient.MakeBucketAsync(makeBucketArgs);
            }

            // Upload file with provided key
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(key)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType);

            if (metadata != null && metadata.Count > 0)
            {
                putObjectArgs = putObjectArgs.WithHeaders(metadata.ToDictionary(kv => kv.Key, kv => kv.Value));
            }

            await _minioClient.PutObjectAsync(putObjectArgs);

            _logger.LogInformation("Uploaded file to MinIO: {Key}", key);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to MinIO");
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string key)
    {
        return await DownloadMediaAsync(key);
    }

    public async Task<IReadOnlyList<StoredFileInfo>> ListFilesAsync(CancellationToken ct = default)
    {
        var results = new List<StoredFileInfo>();
        try
        {
            var listArgs = new ListObjectsArgs()
                .WithBucket(_bucketName)
                .WithRecursive(true);

            var items = new List<Minio.DataModel.Item>();
            await foreach (var item in _minioClient.ListObjectsEnumAsync(listArgs, ct))
            {
                items.Add(item);
            }

            // Fetch stat (user metadata) for each object in parallel, capped at 10 concurrent calls
            var semaphore = new System.Threading.SemaphoreSlim(10, 10);
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var statArgs = new StatObjectArgs()
                        .WithBucket(_bucketName)
                        .WithObject(item.Key);
                    var stat = await _minioClient.StatObjectAsync(statArgs, ct);

                    var userMeta = stat.MetaData != null
                        ? (IReadOnlyDictionary<string, string>)stat.MetaData
                            .Where(kv => kv.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase)
                                      || kv.Key.StartsWith("X-Amz-Meta-", StringComparison.Ordinal))
                            .ToDictionary(
                                kv => kv.Key.Replace("x-amz-meta-", "", StringComparison.OrdinalIgnoreCase),
                                kv => kv.Value,
                                StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>();

                    var fileName = userMeta.TryGetValue("sgm-filename", out var fn) && !string.IsNullOrWhiteSpace(fn)
                        ? fn
                        : System.IO.Path.GetFileName(item.Key);

                    return new StoredFileInfo(
                        Key: item.Key,
                        FileName: fileName,
                        ContentType: stat.ContentType ?? "application/octet-stream",
                        Size: stat.Size,
                        LastModified: stat.LastModified,
                        UserMetadata: userMeta);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to stat MinIO object {Key}; skipping", item.Key);
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var infos = await System.Threading.Tasks.Task.WhenAll(tasks);
            results.AddRange(infos.OfType<StoredFileInfo>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files from MinIO bucket {Bucket}", _bucketName);
        }
        return results;
    }

    public Task<(string PresignedUrl, string Key)?> GetPresignedUploadUrlAsync(string fileName, string contentType, TimeSpan expiry)
        => Task.FromResult<(string, string)?>(null); // Not supported on MinIO — fall back to proxied upload
}
