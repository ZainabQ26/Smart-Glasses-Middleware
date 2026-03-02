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
    private readonly string _bucketName;
    private readonly ILogger<AwsS3MediaStorageService> _logger;

    public AwsS3MediaStorageService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<AwsS3MediaStorageService> logger)
    {
        _s3Client = s3Client;
        _bucketName = configuration["AWS:S3:MediaBucket"] ?? throw new InvalidOperationException("AWS:S3:MediaBucket is not configured");
        _logger = logger;
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName, string contentType)
    {
        if (fileStream.CanSeek) fileStream.Position = 0;
        var key = $"{Guid.NewGuid()}/{fileName}";
        return await UploadFileAsync(key, fileStream, contentType);
    }

    public async Task<string> UploadFileAsync(string key, Stream fileStream, string contentType)
    {
        try
        {
            // Ensure we read from the beginning of the stream
            if (fileStream.CanSeek) fileStream.Position = 0;

            // Guard against uploading empty content
            long streamLength = fileStream.CanSeek ? fileStream.Length : -1;
            if (streamLength == 0)
                throw new InvalidOperationException($"Refusing to upload 0-byte stream to S3 key {key}");

            _logger.LogInformation("Uploading to S3: {Bucket}/{Key} ({Bytes} bytes)", _bucketName, key, streamLength);

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
                BucketName = _bucketName,
                Key = key,
                InputStream = ms,
                ContentType = contentType,
                AutoCloseStream = false
            };

            await _s3Client.PutObjectAsync(request);
            _logger.LogInformation("Uploaded file to S3: {Bucket}/{Key} ({Bytes} bytes)", _bucketName, key, ms.Length);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to S3: {Bucket}/{Key}", _bucketName, key);
            throw;
        }
    }

    public async Task<Stream> DownloadMediaAsync(string key) => await DownloadFileAsync(key);

    public async Task<Stream> DownloadFileAsync(string key)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            var response = await _s3Client.GetObjectAsync(request);

            // Copy to MemoryStream so the caller owns the stream lifetime
            var memStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memStream);
            memStream.Position = 0;

            _logger.LogInformation("Downloaded file from S3: {Bucket}/{Key} ({Bytes} bytes)", _bucketName, key, memStream.Length);
            return memStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from S3: {Bucket}/{Key}", _bucketName, key);
            throw;
        }
    }

    public async Task DeleteMediaAsync(string key)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(request);
            _logger.LogInformation("Deleted file from S3: {Bucket}/{Key}", _bucketName, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from S3: {Bucket}/{Key}", _bucketName, key);
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
}
