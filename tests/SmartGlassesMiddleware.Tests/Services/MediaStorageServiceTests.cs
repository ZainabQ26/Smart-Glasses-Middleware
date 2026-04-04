using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class MediaStorageServiceTests
{
    private MinioMediaStorageService _mediaStorageService;
    private Mock<IMinioClient> _minioClientMock;
    private Mock<IConfiguration> _configurationMock;
    private Mock<ILogger<MinioMediaStorageService>> _loggerMock;

    [SetUp]
    public void Setup()
    {
        _minioClientMock = new Mock<IMinioClient>();
        _configurationMock = new Mock<IConfiguration>();
        _configurationMock.Setup(c => c["MinIO:BucketName"]).Returns("test-bucket");
        _loggerMock = new Mock<ILogger<MinioMediaStorageService>>();

        _mediaStorageService = new MinioMediaStorageService(
            _minioClientMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public void ComputeContentHash_ReturnsExpectedHash()
    {
        // Arrange
        var content = "Hello, World!";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);

        string expectedHash;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(bytes);
            expectedHash = Convert.ToHexString(hashBytes);
        }

        // Act
        var actualHash = _mediaStorageService.ComputeContentHash(stream);

        // Assert
        Assert.That(actualHash, Is.EqualTo(expectedHash));
    }

    [Test]
    public void ComputeContentHash_RestoresStreamPosition()
    {
        // Arrange
        var content = "Testing position restoration";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        stream.Position = 5; // Set an initial position

        // Act
        _mediaStorageService.ComputeContentHash(stream);

        // Assert
        Assert.That(stream.Position, Is.EqualTo(5), "Stream position should be restored to its original value.");
    }

    [Test]
    public void ComputeContentHash_EmptyStream_ReturnsExpectedHash()
    {
        // Arrange
        using var stream = new MemoryStream();

        string expectedHash;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(Array.Empty<byte>());
            expectedHash = Convert.ToHexString(hashBytes);
        }

        // Act
        var actualHash = _mediaStorageService.ComputeContentHash(stream);

        // Assert
        Assert.That(actualHash, Is.EqualTo(expectedHash));
    }

    [Test]
    public void ComputeContentHash_CalledMultipleTimes_ReturnsSameHash()
    {
        // Arrange
        var content = "Consistency test";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);

        // Act
        var hash1 = _mediaStorageService.ComputeContentHash(stream);
        var hash2 = _mediaStorageService.ComputeContentHash(stream);

        // Assert
        Assert.That(hash1, Is.EqualTo(hash2), "Hashing the same stream multiple times should yield the same result.");
        Assert.That(stream.Position, Is.EqualTo(0)); // Checking position as well just to be safe
    }
}
