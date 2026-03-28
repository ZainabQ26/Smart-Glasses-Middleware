using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Hubs;
using SmartGlassesMiddleware.Services;
using StackExchange.Redis;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class RedisPubSubBridgeTests
{
    private Mock<IConnectionMultiplexer> _redisMock;
    private Mock<ISubscriber> _subscriberMock;
    private Mock<IHubContext<TaskHub>> _hubMock;
    private Mock<ILogger<RedisPubSubBridge>> _loggerMock;
    private RedisPubSubBridgeStatus _status;
    private RedisPubSubBridge _bridge;

    [SetUp]
    public void Setup()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _subscriberMock = new Mock<ISubscriber>();
        _hubMock = new Mock<IHubContext<TaskHub>>();
        _loggerMock = new Mock<ILogger<RedisPubSubBridge>>();
        _status = new RedisPubSubBridgeStatus();

        _redisMock.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_subscriberMock.Object);
        _bridge = new RedisPubSubBridge(_redisMock.Object, _hubMock.Object, _loggerMock.Object, _status);
    }

    [TearDown]
    public void TearDown()
    {
        _bridge?.Dispose();
    }

    [Test]
    public async Task StartAsync_SubscribesToChannel()
    {
        await _bridge.StartAsync(CancellationToken.None);

        _subscriberMock.Verify(s => s.Subscribe(
            It.Is<RedisChannel>(c => c == new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal)),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()
        ), Times.Once);

        Assert.That(_status.IsSubscribed, Is.True);
    }

    [Test]
    public async Task StopAsync_UnsubscribesFromChannel()
    {
        await _bridge.StartAsync(CancellationToken.None);
        await _bridge.StopAsync(CancellationToken.None);

        _subscriberMock.Verify(s => s.Unsubscribe(
            It.Is<RedisChannel>(c => c == new RedisChannel("background:task:completed", RedisChannel.PatternMode.Literal)),
            null,
            It.IsAny<CommandFlags>()
        ), Times.Once);
    }

    [Test]
    public async Task StopAsync_CatchesException()
    {
        await _bridge.StartAsync(CancellationToken.None);

        _subscriberMock.Setup(s => s.Unsubscribe(It.IsAny<RedisChannel>(), null, It.IsAny<CommandFlags>()))
            .Throws(new Exception("test"));

        await _bridge.StopAsync(CancellationToken.None);

        Assert.Pass("Did not throw");
    }
}
