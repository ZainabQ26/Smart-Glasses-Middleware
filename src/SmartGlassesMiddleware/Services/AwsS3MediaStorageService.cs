using Amazon.S3;
using Amazon.S3.Model;
using System.Security.Cryptography;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// AWS S3-backed implementation of IMediaStorageService for production (ECS/Fargate).
/// Uses the ECS task IAM role for credentials — no static keys required.
/// Registered instead of MinioMediaStorageService when AWS:UseLocalStack=false.
/// </summary>
public class AwsS3MediaStorageService : IMediaStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _mediaBucketName;
    private readonly string _documentsBucketName;
    private readonly ILogger<AwsS3MediaStorageService> _logger;

    public AwsS3MediaStorageService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<AwsS3MediaStorageService> logger)
    {
        _s3Client = s3Client;
        _mediaBucketName = ResolveBucketName(configuration, "AWS:S3:MediaBucket", "smart-glasses-dev-media");
        _documentsBucketName = ResolveBucketName(configuration, "AWS:S3:DocumentsBucket", _mediaBucketName);
        _logger = logger;
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName, string contentType, IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (fileStream.CanSeek) fileStream.Position = 0;
        var key = $"{Guid.NewGuid()}/{fileName}";
        return await UploadFileAsync(key, fileStream, contentType, metadata);
    }

    public async Task<string> UploadFileAsync(string key, Stream fileStream, string contentType, IReadOnlyDictionary<string, string>? metadata = null)
    {
        try
        {
            // Ensure we read from the beginning of the stream
            if (fileStream.CanSeek) fileStream.Position = 0;

            // Guard against uploading empty content
            long streamLength = fileStream.CanSeek ? fileStream.Length : -1;
            if (streamLength == 0)
                throw new InvalidOperationException($"Refusing to upload 0-byte stream to S3 key {key}");

            _logger.LogInformation("Uploading to S3: {Bucket}/{Key} ({Bytes} bytes)", _mediaBucketName, key, streamLength);

            // Copy to a MemoryStream so the AWS SDK knows the exact length and won't use
            // chunked transfer encoding which can result in a 0-byte object.
            MemoryStream ms;
            if (fileStream is MemoryStream existing)
            {
                ms = existing;
            }
            else
            {
                ms = new MemoryStream();
                await fileStream.CopyToAsync(ms);
                ms.Position = 0;
            }

            if (ms.Length == 0)
                throw new InvalidOperationException($"Stream was empty after copy for S3 key {key}");

            var request = new PutObjectRequest
            {
                BucketName = _mediaBucketName,
                Key = key,
                InputStream = ms,
                ContentType = contentType,
                AutoCloseStream = false
            };

            // Store user-defined metadata (used on boot to reconstruct ManagedFile records)
            if (metadata != null)
            {
                foreach (var kv in metadata)
                {
                    request.Metadata[kv.Key] = kv.Value;
                }
            }

            await _s3Client.PutObjectAsync(request);
            _logger.LogInformation("Uploaded file to S3: {Bucket}/{Key} ({Bytes} bytes)", _mediaBucketName, key, ms.Length);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to S3: {Bucket}/{Key}", _mediaBucketName, key);
            throw;
        }
    }

    public async Task<Stream> DownloadMediaAsync(string key) => await DownloadFileAsync(key);

    public async Task<Stream> DownloadFileAsync(string key)
    {
        foreach (var bucketName in GetCandidateBuckets())
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                using var response = await _s3Client.GetObjectAsync(request);

                // Copy to MemoryStream so the caller owns the stream lifetime
                var memStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memStream);
                memStream.Position = 0;

                _logger.LogInformation("Downloaded file from S3: {Bucket}/{Key} ({Bytes} bytes)", bucketName, key, memStream.Length);
                return memStream;
            }
            catch (AmazonS3Exception ex) when (IsMissingObject(ex))
            {
                // Try the fallback bucket if this key was stored there previously.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file from S3: {Bucket}/{Key}", bucketName, key);
                throw;
            }
        }

        throw new FileNotFoundException($"File {key} was not found in any configured S3 bucket");
    }

    public async Task DeleteMediaAsync(string key)
    {
        foreach (var bucketName in GetCandidateBuckets())
        {
            try
            {
                var request = new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                await _s3Client.DeleteObjectAsync(request);
                _logger.LogInformation("Deleted file from S3: {Bucket}/{Key}", bucketName, key);
                return;
            }
            catch (AmazonS3Exception ex) when (IsMissingObject(ex))
            {
                // Try the fallback bucket if this key was stored there previously.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file from S3: {Bucket}/{Key}", bucketName, key);
                throw;
            }
        }

        throw new FileNotFoundException($"File {key} was not found in any configured S3 bucket");
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

    public async Task<IReadOnlyList<StoredFileInfo>> ListFilesAsync(CancellationToken ct = default)
    {
        var results = new List<StoredFileInfo>();
        try
        {
            foreach (var bucketName in GetCandidateBuckets())
            {
                string? continuationToken = null;
                do
                {
                    var listRequest = new ListObjectsV2Request
                    {
                        BucketName = bucketName,
                        ContinuationToken = continuationToken
                    };

                    var listResponse = await _s3Client.ListObjectsV2Async(listRequest, ct);

                    // Fetch HEAD metadata for all objects in parallel, capped at 10 concurrent calls
                    var semaphore = new System.Threading.SemaphoreSlim(10, 10);
                    var tasks = listResponse.S3Objects.Select(async obj =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            var headRequest = new GetObjectMetadataRequest
                            {
                                BucketName = bucketName,
                                Key = obj.Key
                            };
                            var head = await _s3Client.GetObjectMetadataAsync(headRequest, ct);

                            // S3 user-defined metadata keys are returned WITHOUT the "x-amz-meta-" prefix
                            var userMeta = head.Metadata.Keys
                                .Cast<string>()
                                .ToDictionary(
                                    k => k,
                                    k => head.Metadata[k],
                                    StringComparer.OrdinalIgnoreCase);

                            var fileName = userMeta.TryGetValue("sgm-filename", out var fn) && !string.IsNullOrWhiteSpace(fn)
                                ? fn
                                : System.IO.Path.GetFileName(obj.Key);

                            return new StoredFileInfo(
                                Key: obj.Key,
                                FileName: fileName,
                                ContentType: head.Headers.ContentType ?? "application/octet-stream",
                                Size: head.ContentLength,
                                LastModified: obj.LastModified,
                                UserMetadata: userMeta);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to head S3 object {Bucket}/{Key}; skipping", bucketName, obj.Key);
                            return null;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    var batch = await System.Threading.Tasks.Task.WhenAll(tasks);
                    results.AddRange(batch.OfType<StoredFileInfo>());

                    continuationToken = listResponse.IsTruncated ? listResponse.NextContinuationToken : null;
                }
                while (continuationToken != null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files from S3 buckets {MediaBucket} / {DocumentsBucket}", _mediaBucketName, _documentsBucketName);
        }
        return results;
    }

    public Task<(string PresignedUrl, string Key)?> GetPresignedUploadUrlAsync(string fileName, string contentType, TimeSpan expiry)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "upload";
        var key = $"{Guid.NewGuid()}/{safeFileName}";
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _mediaBucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(expiry),
            ContentType = contentType
        };
        var url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult<(string, string)?>((url, key));
    }

    private IEnumerable<string> GetCandidateBuckets()
    {
        yield return _mediaBucketName;

        if (!string.Equals(_documentsBucketName, _mediaBucketName, StringComparison.OrdinalIgnoreCase))
        {
            yield return _documentsBucketName;
        }
    }

    private static string ResolveBucketName(IConfiguration configuration, string key, string fallback)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return fallback;
    }

    private static bool IsMissingObject(AmazonS3Exception ex)
    {
        return ex.StatusCode == System.Net.HttpStatusCode.NotFound
            || string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase);
    }
}
