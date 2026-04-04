using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Services;
using Amazon.S3;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System;

namespace SmartGlassesMiddleware.Tests.Services
{
    [TestFixture]
    public class AwsS3MediaStorageServiceTests
    {
        private Mock<IAmazonS3> _mockS3Client;
        private Mock<IConfiguration> _mockConfiguration;
        private Mock<ILogger<AwsS3MediaStorageService>> _mockLogger;

        [SetUp]
        public void Setup()
        {
            _mockS3Client = new Mock<IAmazonS3>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger<AwsS3MediaStorageService>>();

            // Setup configuration to return a bucket name to avoid InvalidOperationException in constructor
            _mockConfiguration.Setup(c => c["AWS:S3:MediaBucket"]).Returns("test-bucket");
        }

        private string CalculateSha256Hex(byte[] data)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(data);
            return Convert.ToHexString(hash);
        }

        [Test]
        public void ComputeContentHash_ReturnsExpectedHash_AndRestoresPosition()
        {
            // Arrange
            var service = new AwsS3MediaStorageService(_mockS3Client.Object, _mockConfiguration.Object, _mockLogger.Object);
            var content = "Hello, World!";
            var bytes = Encoding.UTF8.GetBytes(content);
            var expectedHash = CalculateSha256Hex(bytes);

            using var memoryStream = new MemoryStream(bytes);
            long initialPosition = 0;
            memoryStream.Position = initialPosition;

            // Act
            var actualHash = service.ComputeContentHash(memoryStream);

            // Assert
            Assert.That(actualHash, Is.EqualTo(expectedHash));
            Assert.That(memoryStream.Position, Is.EqualTo(initialPosition));
        }

        [Test]
        public void ComputeContentHash_EmptyStream_ReturnsExpectedHash()
        {
            // Arrange
            var service = new AwsS3MediaStorageService(_mockS3Client.Object, _mockConfiguration.Object, _mockLogger.Object);
            var bytes = Array.Empty<byte>();
            var expectedHash = CalculateSha256Hex(bytes);

            using var memoryStream = new MemoryStream(bytes);

            // Act
            var actualHash = service.ComputeContentHash(memoryStream);

            // Assert
            Assert.That(actualHash, Is.EqualTo(expectedHash));
            Assert.That(memoryStream.Position, Is.EqualTo(0));
        }

        [Test]
        public void ComputeContentHash_WithNonZeroPosition_HashesEntireStream_AndRestoresPosition()
        {
            // Arrange
            var service = new AwsS3MediaStorageService(_mockS3Client.Object, _mockConfiguration.Object, _mockLogger.Object);
            var content = "Hello, World! This is a test of the ComputeContentHash method.";
            var bytes = Encoding.UTF8.GetBytes(content);
            var expectedHash = CalculateSha256Hex(bytes);

            using var memoryStream = new MemoryStream(bytes);

            // Advance position to the middle of the stream
            long initialPosition = 10;
            memoryStream.Position = initialPosition;

            // Act
            var actualHash = service.ComputeContentHash(memoryStream);

            // Assert
            // Hash should match the entire stream, because the method resets position to 0 before hashing
            Assert.That(actualHash, Is.EqualTo(expectedHash));

            // Position should be restored to where it was
            Assert.That(memoryStream.Position, Is.EqualTo(initialPosition));
        }
    }
}