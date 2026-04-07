using Microsoft.Extensions.Configuration;
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
        var inMemorySettings = new Dictionary<string, string?> {
            {"BackgroundTasks:DelayBetweenChecksMs", "100"}
        };

        Microsoft.Extensions.Configuration.IConfiguration configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _queue = new NoOpBackgroundTaskQueue(new Mock<ILogger<NoOpBackgroundTaskQueue>>().Object, configuration);
    }

    [Test]
    public async Task EnqueueAsync_Works()
    {
        var entry = new BackgroundTaskEntry { Id = Guid.NewGuid() };
        await _queue.EnqueueAsync(entry);

        Assert.Pass("Enqueue didn't throw");
    }
}
