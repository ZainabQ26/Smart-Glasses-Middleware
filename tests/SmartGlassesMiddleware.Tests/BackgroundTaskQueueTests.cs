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

    [Fact]
    public async Task DequeueAsync_Returns_Task_From_Queue()
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

        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            PromptText = "test",
            Status = BackgroundTaskStatus.Queued
        };
        var json = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
        mockDb.SetupSequence(d => d.ListLeftPopAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(taskId.ToString()))
            .ReturnsAsync(RedisValue.Null);
        mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        var queue = new BackgroundTaskQueue(mockRedis.Object, mockLogger.Object, mockHub.Object, mockConfig);
        var cts = new System.Threading.CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        // Act
        var result = await queue.DequeueAsync(cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskId, result!.Id);
        Assert.Equal("test", result.PromptText);
    }

    [Fact]
    public void Get_Returns_Existing_Task()
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

        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            PromptText = "test task",
            Status = BackgroundTaskStatus.Processing
        };
        var json = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
        mockDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(new RedisValue(json));

        var queue = new BackgroundTaskQueue(mockRedis.Object, mockLogger.Object, mockHub.Object, mockConfig);

        // Act
        var result = queue.Get(taskId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskId, result!.Id);
        Assert.Equal("test task", result.PromptText);
    }

    [Fact]
    public void Get_Returns_Null_For_NonExistent_Task()
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
        mockDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(RedisValue.Null);

        var queue = new BackgroundTaskQueue(mockRedis.Object, mockLogger.Object, mockHub.Object, mockConfig);

        // Act
        var result = queue.Get(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_Updates_Task_And_Sends_Notifications_When_Completed()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockSubscriber = new Mock<ISubscriber>();
        var mockLogger = new Mock<ILogger<BackgroundTaskQueue>>();
        var mockHub = new Mock<IHubContext<TaskHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        
        var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?>
        {
            {"BackgroundTasks:RetentionSeconds", "60"}
        };
        IConfiguration mockConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
        mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);
        mockDb.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDb.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockSubscriber.Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

        var queue = new BackgroundTaskQueue(mockRedis.Object, mockLogger.Object, mockHub.Object, mockConfig);

        var entry = new BackgroundTaskEntry
        {
            Id = Guid.NewGuid(),
            PromptText = "test",
            Status = BackgroundTaskStatus.Completed,
            Result = "Task completed successfully"
        };

        // Act
        await queue.UpdateAsync(entry);

        // Assert
        mockDb.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()
        ), Times.Once);
        mockDb.Verify(d => d.KeyExpireAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()
        ), Times.Once);
    }
}
