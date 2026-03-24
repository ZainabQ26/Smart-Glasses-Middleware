using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using StackExchange.Redis;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class RedisCacheServiceTests
{
    private Mock<IConnectionMultiplexer> _mockRedis;
    private Mock<ILogger<RedisCacheService>> _mockLogger;
    private Mock<IConfiguration> _mockConfiguration;
    private Mock<IDatabase> _mockDatabase;
    private RedisCacheService _cacheService;

    [SetUp]
    public void Setup()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockLogger = new Mock<ILogger<RedisCacheService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockDatabase = new Mock<IDatabase>();

        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        // Setup configuration mock
        var mockConfSection = new Mock<IConfigurationSection>();
        mockConfSection.Setup(s => s.Value).Returns("500");
        _mockConfiguration.Setup(c => c.GetSection("Cache:RedisOperationTimeoutMs")).Returns(mockConfSection.Object);

        _cacheService = new RedisCacheService(_mockRedis.Object, _mockLogger.Object, _mockConfiguration.Object);
    }

    [Test]
    public async Task GetAsync_WhenValueExists_ShouldReturnValue()
    {
        // Arrange
        var key = "testKey";
        var expectedValue = new TestData { Id = 1, Name = "Test" };
        var serializedValue = JsonSerializer.Serialize(expectedValue);

        // This simulates a fast DB return
        _mockDatabase.Setup(db => db.StringGetAsync(key, CommandFlags.None))
            .ReturnsAsync(new RedisValue(serializedValue));

        // Act
        var result = await _cacheService.GetAsync<TestData>(key);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(expectedValue.Id));
        Assert.That(result.Name, Is.EqualTo(expectedValue.Name));
    }

    [Test]
    public async Task GetAsync_WhenValueDoesNotExist_ShouldReturnDefault()
    {
        // Arrange
        var key = "nonExistentKey";
        _mockDatabase.Setup(db => db.StringGetAsync(key, CommandFlags.None))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _cacheService.GetAsync<TestData>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_WhenRedisThrowsException_ShouldReturnDefault()
    {
        // Arrange
        var key = "errorKey";
        _mockDatabase.Setup(db => db.StringGetAsync(key, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act
        var result = await _cacheService.GetAsync<TestData>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_WhenRedisTimesOut_ShouldReturnDefault()
    {
        // Arrange
        var key = "timeoutKey";

        // Setup the mock to delay longer than the timeout configured (500ms)
        _mockDatabase.Setup(db => db.StringGetAsync(key, CommandFlags.None))
            .Returns(async () =>
            {
                await Task.Delay(1000); // Delay longer than the configured timeout
                return new RedisValue("delayed value");
            });

        // Act
        var result = await _cacheService.GetAsync<TestData>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SetAsync_WhenSuccessful_ShouldCallStringSetAsync()
    {
        // Arrange
        var key = "testKey";
        var value = new TestData { Id = 1, Name = "Test" };
        var expiry = TimeSpan.FromMinutes(5);

        // Act
        await _cacheService.SetAsync(key, value, expiry);

        // Assert
        _mockDatabase.Verify(db => db.StringSetAsync(key, It.IsAny<RedisValue>(), expiry, false, When.Always, CommandFlags.None), Times.Once);
    }

    [Test]
    public async Task SetAsync_WhenRedisThrowsException_ShouldHandleGracefully()
    {
        // Arrange
        var key = "errorKey";
        var value = new TestData { Id = 1, Name = "Test" };

        _mockDatabase.Setup(db => db.StringSetAsync(key, It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), false, When.Always, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert (Should not throw)
        Assert.DoesNotThrowAsync(async () => await _cacheService.SetAsync(key, value));
    }

    [Test]
    public async Task RemoveAsync_WhenSuccessful_ShouldCallKeyDeleteAsync()
    {
        // Arrange
        var key = "testKey";

        _mockDatabase.Setup(db => db.KeyDeleteAsync(key, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await _cacheService.RemoveAsync(key);

        // Assert
        _mockDatabase.Verify(db => db.KeyDeleteAsync(key, CommandFlags.None), Times.Once);
    }

    [Test]
    public async Task RemoveAsync_WhenRedisThrowsException_ShouldHandleGracefully()
    {
        // Arrange
        var key = "errorKey";

        _mockDatabase.Setup(db => db.KeyDeleteAsync(key, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert (Should not throw)
        Assert.DoesNotThrowAsync(async () => await _cacheService.RemoveAsync(key));
    }

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
