using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class InMemoryCacheServiceTests
{
    private InMemoryCacheService _service;
    private Mock<ILogger<InMemoryCacheService>> _loggerMock;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<InMemoryCacheService>>();
        _service = new InMemoryCacheService(_loggerMock.Object);
    }

    [Test]
    public async Task GetSetRemove_Works()
    {
        var key = "test-key";
        var val = "test-val";

        await _service.SetAsync(key, val, TimeSpan.FromMinutes(1));
        var retrieved = await _service.GetAsync<string>(key);
        Assert.That(retrieved, Is.EqualTo(val));

        await _service.RemoveAsync(key);
        var missing = await _service.GetAsync<string>(key);
        Assert.That(missing, Is.Null);
    }

    [Test]
    public async Task GetAsync_Exception_ReturnsDefaultAndLogsError()
    {
        // Passing null will throw ArgumentNullException in ConcurrentDictionary
        var result = await _service.GetAsync<string>(null!);

        Assert.That(result, Is.Null);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v!.ToString()!.Contains("InMemoryCacheService GetAsync error for key")),
                It.IsAny<ArgumentNullException>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }
}
