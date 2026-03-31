using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class NoOpBackgroundTaskQueueTests
{
    private NoOpBackgroundTaskQueue _queue;

    [SetUp]
    public void Setup()
    {
        _queue = new NoOpBackgroundTaskQueue(new Mock<ILogger<NoOpBackgroundTaskQueue>>().Object);
    }

    [Test]
    public async Task EnqueueAsync_Works()
    {
        var entry = new BackgroundTaskEntry { Id = Guid.NewGuid() };
        await _queue.EnqueueAsync(entry);

        Assert.Pass("Enqueue didn't throw");
    }
}
