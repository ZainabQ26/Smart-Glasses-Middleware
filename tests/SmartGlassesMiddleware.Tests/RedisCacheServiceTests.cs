using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class RedisCacheServiceTests
    {
        private Mock<IConnectionMultiplexer> _mockRedis;
        private Mock<IDatabase> _mockDb;
        private Mock<ILogger<RedisCacheService>> _mockLogger;
        private IConfiguration _configuration;
        private RedisCacheService _cacheService;

        [SetUp]
        public void Setup()
        {
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockDb = new Mock<IDatabase>();
            _mockLogger = new Mock<ILogger<RedisCacheService>>();

            var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?> {
                {"Cache:RedisOperationTimeoutMs", "300"}
            };

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockDb.Object);

            _cacheService = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _configuration);
        }

        [Test]
        public async Task SetAsync_SuccessfullySetsValue()
        {
            var key = "testKey";
            var value = new { Name = "Test" };
            var expectedJson = JsonSerializer.Serialize(value);
            TimeSpan? expiry = TimeSpan.FromMinutes(5);

            _mockDb.Setup(db => db.StringSetAsync(
                It.Is<RedisKey>(k => k == key),
                It.Is<RedisValue>(v => v == expectedJson),
                expiry,
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            await _cacheService.SetAsync(key, value, expiry);

            _mockDb.Verify(db => db.StringSetAsync(
                It.Is<RedisKey>(k => k == key),
                It.Is<RedisValue>(v => v == expectedJson),
                expiry,
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Test]
        public async Task SetAsync_HandlesException()
        {
            var key = "testKey";
            var value = new { Name = "Test" };
            TimeSpan? expiry = TimeSpan.FromMinutes(5);

            var exception = new Exception("Redis connection failed");
            _mockDb.Setup(db => db.StringSetAsync(
                It.Is<RedisKey>(k => k == key),
                It.IsAny<RedisValue>(),
                expiry,
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
                .ThrowsAsync(exception);

            await _cacheService.SetAsync(key, value, expiry);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Error setting value in Redis cache for key: {key}")),
                    exception,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Test]
        public async Task SetAsync_Timeout_LogsWarning()
        {
            var key = "testKey";
            var value = new { Name = "Test" };
            TimeSpan? expiry = TimeSpan.FromMinutes(5);

            _mockDb.Setup(db => db.StringSetAsync(
                It.Is<RedisKey>(k => k == key),
                It.IsAny<RedisValue>(),
                expiry,
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
                .Returns(async () => {
                    await Task.Delay(500); // longer than the 300ms timeout
                    return true;
                });

            await _cacheService.SetAsync(key, value, expiry);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Redis SET timed out for key: {key}")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
