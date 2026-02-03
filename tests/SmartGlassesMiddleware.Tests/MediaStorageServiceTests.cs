using System;
using System.IO;
using System.Text;
using Xunit;
using Moq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class MediaStorageServiceTests
{
    [Fact]
    public void ComputeContentHash_Returns_Same_Hash_For_Same_Content()
    {
        // Arrange
        var mockClient = new Mock<IMinioClient>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<MinioMediaStorageService>>();
        
        mockConfig.Setup(c => c["MinIO:BucketName"]).Returns("test-bucket");
        
        var service = new MinioMediaStorageService(mockClient.Object, mockConfig.Object, mockLogger.Object);
        
        var content = "This is a test file content";
        var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Act
        var hash1 = service.ComputeContentHash(stream1);
        var hash2 = service.ComputeContentHash(stream2);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
    }

    [Fact]
    public void ComputeContentHash_Returns_Different_Hash_For_Different_Content()
    {
        // Arrange
        var mockClient = new Mock<IMinioClient>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<MinioMediaStorageService>>();
        
        mockConfig.Setup(c => c["MinIO:BucketName"]).Returns("test-bucket");
        
        var service = new MinioMediaStorageService(mockClient.Object, mockConfig.Object, mockLogger.Object);
        
        var content1 = "This is test content 1";
        var content2 = "This is test content 2";
        var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content1));
        var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content2));

        // Act
        var hash1 = service.ComputeContentHash(stream1);
        var hash2 = service.ComputeContentHash(stream2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeContentHash_Resets_Stream_Position()
    {
        // Arrange
        var mockClient = new Mock<IMinioClient>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<MinioMediaStorageService>>();
        
        mockConfig.Setup(c => c["MinIO:BucketName"]).Returns("test-bucket");
        
        var service = new MinioMediaStorageService(mockClient.Object, mockConfig.Object, mockLogger.Object);
        
        var content = "This is a test file content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        stream.Position = 10; // Move position

        // Act
        var hash = service.ComputeContentHash(stream);

        // Assert
        Assert.Equal(10, stream.Position); // Should be restored
    }
}
