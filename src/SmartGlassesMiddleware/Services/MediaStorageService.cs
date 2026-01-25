using Minio;
using Minio.DataModel.Args;

namespace SmartGlassesMiddleware.Services;

public interface IMediaStorageService
{
    Task<string> UploadMediaAsync(Stream fileStream, string fileName, string contentType);
    Task<Stream> DownloadMediaAsync(string key);
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

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName, string contentType)
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
}
