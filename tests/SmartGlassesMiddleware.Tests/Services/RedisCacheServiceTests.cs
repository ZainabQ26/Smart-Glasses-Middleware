using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Services;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class RedisCacheServiceTests
    {
        private Mock<IConnectionMultiplexer> _mockRedis;
        private Mock<IDatabase> _mockDb;
        private Mock<ILogger<RedisCacheService>> _mockLogger;
        private Mock<IConfiguration> _mockConfiguration;

        [SetUp]
        public void Setup()
        {
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockDb = new Mock<IDatabase>();
            _mockLogger = new Mock<ILogger<RedisCacheService>>();
            _mockConfiguration = new Mock<IConfiguration>();

            var configurationSection = new Mock<IConfigurationSection>();
            configurationSection.Setup(s => s.Value).Returns("100"); // Set timeout to 100ms

            _mockConfiguration.Setup(c => c.GetSection("Cache:RedisOperationTimeoutMs"))
                .Returns(configurationSection.Object);

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockDb.Object);
        }

        [Test]
        public async Task GetAsync_Timeout_ReturnsDefault()
        {
            // Arrange
            var key = "testKey";

            // Create a task that will not complete within the timeout (100ms)
            var tcs = new TaskCompletionSource<RedisValue>();

            _mockDb.Setup(db => db.StringGetAsync(key, CommandFlags.None))
                .Returns(tcs.Task);

            var cacheService = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _mockConfiguration.Object);

            // Act
            var result = await cacheService.GetAsync<string>(key);

            // Assert
            Assert.That(result, Is.Null);

            // Verify logger was called with timeout warning
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v!.ToString()!.Contains("Redis GET timed out")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }

        [Test]
        public async Task GetAsync_Success_ReturnsDeserializedValue()
        {
            // Arrange
            var key = "testKey";
            var expectedValue = new TestObject { Id = 1, Name = "Test" };
            var serializedValue = JsonSerializer.Serialize(expectedValue);

            _mockDb.Setup(db => db.StringGetAsync(key, CommandFlags.None))
                .ReturnsAsync((RedisValue)serializedValue);

            var cacheService = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _mockConfiguration.Object);

            // Act
            var result = await cacheService.GetAsync<TestObject>(key);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(result.Name, Is.EqualTo(expectedValue.Name));
        }

        [Test]
        public async Task GetAsync_Exception_ReturnsDefault()
        {
            // Arrange
            var key = "testKey";

            _mockDb.Setup(db => db.StringGetAsync(key, CommandFlags.None))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated connection error"));

            var cacheService = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _mockConfiguration.Object);

            // Act
            var result = await cacheService.GetAsync<string>(key);

            // Assert
            Assert.That(result, Is.Null);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v!.ToString()!.Contains("Error getting value from Redis cache")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }

        [Test]
        public async Task SetAsync_Timeout_LogsWarning()
        {
            // Arrange
            var key = "testKey";
            var value = "testValue";

            var tcs = new TaskCompletionSource<bool>();

            _mockDb.Setup(db => db.StringSetAsync(key, It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), false, When.Always, CommandFlags.None))
                .Returns(tcs.Task);

            var cacheService = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _mockConfiguration.Object);

            // Act
            await cacheService.SetAsync(key, value);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v!.ToString()!.Contains("Redis SET timed out")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }

        [Test]
        public async Task RemoveAsync_Timeout_LogsWarning()
        {
            // Arrange
            var key = "testKey";

            var tcs = new TaskCompletionSource<bool>();

            _mockDb.Setup(db => db.KeyDeleteAsync(key, CommandFlags.None))
                .Returns(tcs.Task);

            var cacheService = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _mockConfiguration.Object);

            // Act
            await cacheService.RemoveAsync(key);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v!.ToString()!.Contains("Redis DELETE timed out")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }

        private class TestObject
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
