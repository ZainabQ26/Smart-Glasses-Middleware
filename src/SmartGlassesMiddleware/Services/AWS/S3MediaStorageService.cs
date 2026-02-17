using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using System.Security.Cryptography;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// AWS S3 implementation of media storage service
/// </summary>
public class S3MediaStorageService : IMediaStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _mediaBucket;
    private readonly string _documentsBucket;
    private readonly ILogger<S3MediaStorageService> _logger;
    private readonly TransferUtility _transferUtility;

    public S3MediaStorageService(
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<S3MediaStorageService> logger)
    {
        _s3Client = s3Client;
        _logger = logger;
        
        _mediaBucket = configuration["AWS:S3:MediaBucket"] 
            ?? throw new InvalidOperationException("AWS:S3:MediaBucket configuration is required");
        _documentsBucket = configuration["AWS:S3:DocumentsBucket"] 
            ?? throw new InvalidOperationException("AWS:S3:DocumentsBucket configuration is required");

        _transferUtility = new TransferUtility(_s3Client);
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName, string contentType)
    {
        var key = $"media/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}/{fileName}";
        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = fileStream,
            Key = key,
            BucketName = _mediaBucket,
            ContentType = contentType,
            CannedACL = S3CannedACL.Private
        };
        await _transferUtility.UploadAsync(uploadRequest);
        return key;
    }

    public async Task<Stream> DownloadMediaAsync(string key)
    {
        var request = new GetObjectRequest { BucketName = _mediaBucket, Key = key };
        var response = await _s3Client.GetObjectAsync(request);
        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task DeleteMediaAsync(string key)
    {
        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _mediaBucket, Key = key });
    }

    public string ComputeContentHash(Stream fileStream)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(fileStream);
        fileStream.Position = 0;
        return Convert.ToBase64String(hash);
    }

    public async Task<string> UploadFileAsync(string key, Stream fileStream, string contentType)
    {
        var bucket = contentType.Contains("document") || contentType.Contains("pdf") || contentType.Contains("word")
            ? _documentsBucket : _mediaBucket;
        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = fileStream,
            Key = key,
            BucketName = bucket,
            ContentType = contentType
        };
        await _transferUtility.UploadAsync(uploadRequest);
        return key;
    }

    public async Task<Stream> DownloadFileAsync(string key)
    {
        try
        {
            var request = new GetObjectRequest { BucketName = _mediaBucket, Key = key };
            var response = await _s3Client.GetObjectAsync(request);
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (AmazonS3Exception)
        {
            var request = new GetObjectRequest { BucketName = _documentsBucket, Key = key };
            var response = await _s3Client.GetObjectAsync(request);
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }
    }
}
