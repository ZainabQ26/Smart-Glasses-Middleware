using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Hubs;

namespace SmartGlassesMiddleware.Tests;

public class BackgroundTaskQueueTests
{
    [Fact]
    public async Task EnqueueAsync_Stores_Task_In_Redis()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<BackgroundTaskQueue>>();
        var mockHub = new Mock<IHubContext<TaskHub>>();
        
        var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?>
        {
            {"BackgroundTasks:RetentionSeconds", "60"}
        };
        IConfiguration mockConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
        mockDb.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDb.Setup(d => d.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var queue = new BackgroundTaskQueue(mockRedis.Object, mockLogger.Object, mockHub.Object, mockConfig);

        var entry = new BackgroundTaskEntry
        {
            PromptText = "test"
        };

        // Act
        await queue.EnqueueAsync(entry);

        // Assert
        mockDb.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()
        ), Times.Once);

        mockDb.Verify(d => d.ListRightPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()
        ), Times.Once);
    }
}
