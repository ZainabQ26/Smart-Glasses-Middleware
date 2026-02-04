using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class CacheServiceTests
{
    private Mock<IConnectionMultiplexer> CreateMockRedis(Mock<IDatabase> mockDb)
    {
        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDb.Object);
        return mockRedis;
    }

    [Fact]
    public async Task GetAsync_Returns_Null_When_Key_Does_Not_Exist()
    {
        // Arrange
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act
        var result = await service.GetAsync<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_Returns_Deserialized_Value_When_Key_Exists()
    {
        // Arrange
        var testObject = new { Name = "Test", Value = 123 };
        var serialized = JsonSerializer.Serialize(testObject);
        
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)serialized);

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act
        var result = await service.GetAsync<dynamic>("testkey");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.GetProperty("Name").GetString());
        Assert.Equal(123, result.GetProperty("Value").GetInt32());
    }

    [Fact]
    public async Task GetAsync_Returns_Default_On_Exception()
    {
        // Arrange
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection failed"));

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act
        var result = await service.GetAsync<string>("testkey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_Stores_Serialized_Value_Without_Expiry()
    {
        // Arrange
        var testObject = new { Name = "Test", Value = 123 };
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            null,
            false,
            When.Always,
            CommandFlags.None))
            .ReturnsAsync(true);

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act
        await service.SetAsync("testkey", testObject);

        // Assert
        mockDb.Verify(db => db.StringSetAsync(
            It.Is<RedisKey>(k => k == "testkey"),
            It.Is<RedisValue>(v => v.ToString().Contains("Test")),
            null,
            false,
            When.Always,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task SetAsync_Stores_Serialized_Value_With_Expiry()
    {
        // Arrange
        var testObject = new { Name = "Test", Value = 123 };
        var expiry = TimeSpan.FromMinutes(5);
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            false,
            When.Always,
            CommandFlags.None))
            .ReturnsAsync(true);

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act
        await service.SetAsync("testkey", testObject, expiry);

        // Assert
        mockDb.Verify(db => db.StringSetAsync(
            It.Is<RedisKey>(k => k == "testkey"),
            It.IsAny<RedisValue>(),
            expiry,
            false,
            When.Always,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task SetAsync_Does_Not_Throw_On_Exception()
    {
        // Arrange
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            false,
            When.Always,
            CommandFlags.None))
            .ThrowsAsync(new RedisException("Connection failed"));

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => service.SetAsync("testkey", "value"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RemoveAsync_Deletes_Key()
    {
        // Arrange
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act
        await service.RemoveAsync("testkey");

        // Assert
        mockDb.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey>(k => k == "testkey"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_Does_Not_Throw_On_Exception()
    {
        // Arrange
        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection failed"));

        var mockRedis = CreateMockRedis(mockDb);
        var mockLogger = new Mock<ILogger<RedisCacheService>>();
        var service = new RedisCacheService(mockRedis.Object, mockLogger.Object);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => service.RemoveAsync("testkey"));
        Assert.Null(exception);
    }
}
