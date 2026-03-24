using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class NoOpBackgroundTaskQueueTests
{
    private Mock<ILogger<NoOpBackgroundTaskQueue>> _loggerMock;
    private NoOpBackgroundTaskQueue _queue;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<NoOpBackgroundTaskQueue>>();
        _queue = new NoOpBackgroundTaskQueue(_loggerMock.Object);
    }

    [Test]
    public void Get_WhenQueueIsEmpty_ReturnsNull()
    {
        // Act
        var result = _queue.Get(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Get_WhenIdDoesNotExist_ReturnsNull()
    {
        // Arrange
        var entry = new BackgroundTaskEntry { Id = Guid.NewGuid() };
        await _queue.EnqueueAsync(entry);

        // Act
        var result = _queue.Get(Guid.NewGuid()); // Look up a different random ID

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Get_WhenIdExists_ReturnsEntry()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entry = new BackgroundTaskEntry { Id = id };
        await _queue.EnqueueAsync(entry);

        // Act
        var result = _queue.Get(id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(entry));
        Assert.That(result!.Id, Is.EqualTo(id));
    }

    [Test]
    public async Task Get_AfterUpdateAsync_ReturnsUpdatedEntry()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entry = new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Queued };
        await _queue.EnqueueAsync(entry);

        // Act - Update the entry status
        var updatedEntry = new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Completed };
        await _queue.UpdateAsync(updatedEntry);

        // Retrieve again
        var result = _queue.Get(id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(BackgroundTaskStatus.Completed));
        Assert.That(result, Is.EqualTo(updatedEntry));
    }
}
