using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class RedisCacheServiceBehaviorTests
{
    private Mock<IConnectionMultiplexer> _redisMock;
    private Mock<IDatabase> _dbMock;
    private Mock<ILogger<RedisCacheService>> _loggerMock;
    private Mock<IConfiguration> _configMock;
    private Mock<IConfigurationSection> _configSectionMock;
    private RedisCacheService _cacheService;

    [SetUp]
    public void SetUp()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<RedisCacheService>>();
        _configMock = new Mock<IConfiguration>();
        _configSectionMock = new Mock<IConfigurationSection>();

        // Set up the configuration to return 300 ms for Cache:RedisOperationTimeoutMs
        _configSectionMock.Setup(s => s.Value).Returns("300");
        _configMock.Setup(c => c.GetSection("Cache:RedisOperationTimeoutMs")).Returns(_configSectionMock.Object);

        // Set up the multiplexer to return our mocked IDatabase
        _redisMock.Setup(x => x.GetDatabase(-1, null)).Returns(_dbMock.Object);

        _cacheService = new RedisCacheService(_redisMock.Object, _loggerMock.Object, _configMock.Object);
    }

    [Test]
    public async Task GetAsync_WhenValueExists_ReturnsDeserializedValue()
    {
        // Arrange
        var key = "test-key";
        var expectedObject = new { Name = "Test", Value = 42 };
        var serializedValue = JsonSerializer.Serialize(expectedObject);
        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync(serializedValue);

        // Act
        var result = await _cacheService.GetAsync<dynamic>(key);
        var jsonResult = JsonSerializer.Serialize(result);

        // Assert
        Assert.That(jsonResult, Is.EqualTo(serializedValue));
    }

    [Test]
    public async Task GetAsync_WhenValueIsNullOrEmpty_ReturnsDefault()
    {
        // Arrange
        var key = "test-key";
        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync(RedisValue.EmptyString);

        // Act
        var result = await _cacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_WhenTimeoutOccurs_ReturnsDefaultAndLogsWarning()
    {
        // Arrange
        var key = "test-key";

        // We use TaskCompletionSource to simulate a task that never completes (or takes longer than timeout)
        var tcs = new TaskCompletionSource<RedisValue>();
        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .Returns(tcs.Task);

        // Act
        var result = await _cacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.Null, "GetAsync should return default (null) when timeout occurs.");

        // Verify that the logger was called with LogWarning
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Redis GET timed out for key: {key}")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task GetAsync_WhenDatabaseThrowsException_ReturnsDefaultAndLogsError()
    {
        // Arrange
        var key = "test-key";
        var expectedException = new Exception("Test Redis Exception");
        _dbMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ThrowsAsync(expectedException);

        // Act
        var result = await _cacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.Null, "GetAsync should return default (null) when an exception is thrown.");

        // Verify that the logger was called with LogError
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Error getting value from Redis cache for key: {key}")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task RemoveAsync_Works()
    {
        var key = "test";
        _dbMock.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
        await _cacheService.RemoveAsync(key);
        _dbMock.Verify(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task SetAsync_Works()
    {
        var key = "test";
        _dbMock.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
        await _cacheService.SetAsync(key, "value");
        _dbMock.Verify(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }
}
