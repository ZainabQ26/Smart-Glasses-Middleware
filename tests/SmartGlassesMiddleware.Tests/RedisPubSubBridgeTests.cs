using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Hubs;

namespace SmartGlassesMiddleware.Tests;

public class RedisPubSubBridgeTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<ISubscriber> _mockSubscriber;
    private readonly Mock<IHubContext<TaskHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<ILogger<RedisPubSubBridge>> _mockLogger;
    private readonly RedisPubSubBridgeStatus _status;

    public RedisPubSubBridgeTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockSubscriber = new Mock<ISubscriber>();
        _mockHubContext = new Mock<IHubContext<TaskHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockLogger = new Mock<ILogger<RedisPubSubBridge>>();
        _status = new RedisPubSubBridgeStatus();

        _mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_mockSubscriber.Object);
        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.All).Returns(_mockClientProxy.Object);
    }

    [Fact]
    public async Task StartAsync_ShouldSubscribeToRedisChannel()
    {
        // Arrange
        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        _mockSubscriber.Verify(
            s => s.Subscribe(
                It.Is<RedisChannel>(ch => ch.ToString() == "background:task:completed"),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldLogSubscription()
    {
        // Arrange
        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => (v.ToString() ?? "").Contains("RedisPubSubBridge subscribed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldSetStatusToSubscribed()
    {
        // Arrange
        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.True(_status.IsSubscribed);
        Assert.Null(_status.LastError);
        Assert.NotNull(_status.LastUpdatedUtc);
    }

    [Fact]
    public async Task MessageReceived_ShouldForwardToSignalR()
    {
        // Arrange
        Action<RedisChannel, RedisValue>? messageHandler = null;

        _mockSubscriber
            .Setup(s => s.Subscribe(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback((RedisChannel ch, Action<RedisChannel, RedisValue> handler, CommandFlags flags) =>
            {
                messageHandler = handler;
            });

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        var payload = new { taskId = "123", status = "completed" };
        var message = JsonSerializer.Serialize(payload);

        // Act
        messageHandler?.Invoke(new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal), message);

        // Wait for async processing
        await Task.Delay(100);

        // Assert
        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "TaskCompleted",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MessageReceived_ShouldUpdateStatusOnSuccess()
    {
        // Arrange
        Action<RedisChannel, RedisValue>? messageHandler = null;

        _mockSubscriber
            .Setup(s => s.Subscribe(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback((RedisChannel ch, Action<RedisChannel, RedisValue> handler, CommandFlags flags) =>
            {
                messageHandler = handler;
            });

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        var payload = new { taskId = "123", status = "completed" };
        var message = JsonSerializer.Serialize(payload);

        // Act
        messageHandler?.Invoke(new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal), message);

        await Task.Delay(100);

        // Assert
        Assert.True(_status.IsSubscribed);
        Assert.Null(_status.LastError);
        Assert.NotNull(_status.LastUpdatedUtc);
    }

    [Fact]
    public async Task MessageReceived_ShouldHandleInvalidJson()
    {
        // Arrange
        Action<RedisChannel, RedisValue>? messageHandler = null;

        _mockSubscriber
            .Setup(s => s.Subscribe(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback((RedisChannel ch, Action<RedisChannel, RedisValue> handler, CommandFlags flags) =>
            {
                messageHandler = handler;
            });

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        var invalidMessage = "{ invalid json";

        // Act
        messageHandler?.Invoke(new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal), invalidMessage);

        await Task.Delay(100);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => (v.ToString() ?? "").Contains("Failed to forward pubsub message")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        Assert.False(_status.IsSubscribed);
        Assert.NotNull(_status.LastError);
    }

    [Fact]
    public async Task MessageReceived_ShouldHandleSignalRFailure()
    {
        // Arrange
        Action<RedisChannel, RedisValue>? messageHandler = null;

        _mockSubscriber
            .Setup(s => s.Subscribe(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback((RedisChannel ch, Action<RedisChannel, RedisValue> handler, CommandFlags flags) =>
            {
                messageHandler = handler;
            });

        _mockClientProxy
            .Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SignalR connection failed"));

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        var payload = new { taskId = "123", status = "completed" };
        var message = JsonSerializer.Serialize(payload);

        // Act
        messageHandler?.Invoke(new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal), message);

        await Task.Delay(100);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => (v.ToString() ?? "").Contains("Failed to forward pubsub message")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        Assert.False(_status.IsSubscribed);
        Assert.Equal("SignalR connection failed", _status.LastError);
    }

    [Fact]
    public async Task StopAsync_ShouldUnsubscribeFromChannel()
    {
        // Arrange
        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert
        _mockSubscriber.Verify(
            s => s.Unsubscribe(
                It.Is<RedisChannel>(ch => ch.ToString() == "background:task:completed"),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task StopAsync_ShouldHandleUnsubscribeError()
    {
        // Arrange
        _mockSubscriber
            .Setup(s => s.Unsubscribe(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Unsubscribe failed"));

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert - should not throw
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => (v.ToString() ?? "").Contains("Error unsubscribing")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);

        // Act & Assert - should not throw
        service.Dispose();
    }

    [Fact]
    public async Task MessageReceived_WithComplexPayload_ShouldForwardCorrectly()
    {
        // Arrange
        Action<RedisChannel, RedisValue>? messageHandler = null;
        object? capturedPayload = null;

        _mockSubscriber
            .Setup(s => s.Subscribe(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback((RedisChannel ch, Action<RedisChannel, RedisValue> handler, CommandFlags flags) =>
            {
                messageHandler = handler;
            });

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        var complexPayload = new
        {
            taskId = "abc-123",
            status = "completed",
            result = new
            {
                data = "test data",
                timestamp = DateTime.UtcNow
            }
        };
        var message = JsonSerializer.Serialize(complexPayload);

        // Act
        messageHandler?.Invoke(new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal), message);

        await Task.Delay(100);

        // Assert
        Assert.NotNull(capturedPayload);
        _mockClientProxy.Verify(
            c => c.SendCoreAsync(
                "TaskCompleted",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MultipleMessages_ShouldAllBeForwarded()
    {
        // Arrange
        Action<RedisChannel, RedisValue>? messageHandler = null;
        var forwardedCount = 0;

        _mockSubscriber
            .Setup(s => s.Subscribe(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback((RedisChannel ch, Action<RedisChannel, RedisValue> handler, CommandFlags flags) =>
            {
                messageHandler = handler;
            });

        _mockClientProxy
            .Setup(c => c.SendCoreAsync(
                "TaskCompleted",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => forwardedCount++)
            .Returns(Task.CompletedTask);

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);
        await service.StartAsync(CancellationToken.None);

        // Act
        for (int i = 0; i < 5; i++)
        {
            var payload = new { taskId = $"task-{i}", status = "completed" };
            var message = JsonSerializer.Serialize(payload);
            messageHandler?.Invoke(new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal), message);
        }

        await Task.Delay(200);

        // Assert
        Assert.Equal(5, forwardedCount);
    }

    [Fact]
    public async Task StartAsync_WithoutSubscriber_ShouldNotThrow()
    {
        // Arrange
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        _mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(default(ISubscriber));
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

        var service = new RedisPubSubBridge(_mockRedis.Object, _mockHubContext.Object, _mockLogger.Object, _status);

        // Act & Assert - should not throw
        await Assert.ThrowsAsync<NullReferenceException>(
            () => service.StartAsync(CancellationToken.None));
    }
}
