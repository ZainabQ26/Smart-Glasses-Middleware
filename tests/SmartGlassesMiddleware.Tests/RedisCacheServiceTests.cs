using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using SmartGlassesMiddleware.Services;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class RedisCacheServiceTests
    {
        private Mock<IConnectionMultiplexer> _mockRedis;
        private Mock<IDatabase> _mockDatabase;
        private Mock<ILogger<RedisCacheService>> _mockLogger;
        private IConfiguration _configuration;
        private RedisCacheService _service;

        [SetUp]
        public void Setup()
        {
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockDatabase = new Mock<IDatabase>();
            _mockLogger = new Mock<ILogger<RedisCacheService>>();

            var inMemorySettings = new Dictionary<string, string?> {
                {"Cache:RedisOperationTimeoutMs", "100"}
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_mockDatabase.Object);

            _service = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _configuration);
        }

        [Test]
        public async Task RemoveAsync_Success_DeletesKey()
        {
            // Arrange
            var key = "test-key";
            _mockDatabase.Setup(db => db.KeyDeleteAsync(It.Is<RedisKey>(k => k == key), CommandFlags.None))
                .ReturnsAsync(true);

            // Act
            await _service.RemoveAsync(key);

            // Assert
            _mockDatabase.Verify(db => db.KeyDeleteAsync(It.Is<RedisKey>(k => k == key), CommandFlags.None), Times.Once);

            // Verify that no error or warning was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Never);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Never);
        }

        [Test]
        public async Task RemoveAsync_Timeout_LogsWarning()
        {
            // Arrange
            var key = "timeout-key";

            // Setup a task that takes longer than the timeout
            var tcs = new TaskCompletionSource<bool>();
            _mockDatabase.Setup(db => db.KeyDeleteAsync(It.Is<RedisKey>(k => k == key), CommandFlags.None))
                .Returns(tcs.Task); // Task will never complete natively, forcing timeout

            // Act
            await _service.RemoveAsync(key);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Redis DELETE timed out for key: {key}")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }

        [Test]
        public async Task RemoveAsync_Exception_LogsError()
        {
            // Arrange
            var key = "error-key";
            var exception = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed");

            _mockDatabase.Setup(db => db.KeyDeleteAsync(It.Is<RedisKey>(k => k == key), CommandFlags.None))
                .ThrowsAsync(exception);

            // Act
            await _service.RemoveAsync(key);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Error removing value from Redis cache for key: {key}")),
                    It.Is<Exception>(e => e == exception),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }
    }
}
