using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests
{
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
        public async Task DequeueAsync_ReturnsEnqueuedTask()
        {
            // Arrange
            var entry = new BackgroundTaskEntry();
            await _queue.EnqueueAsync(entry);

            // Act
            var dequeuedEntry = await _queue.DequeueAsync(CancellationToken.None);

            // Assert
            Assert.That(dequeuedEntry, Is.Not.Null);
            Assert.That(dequeuedEntry.Id, Is.EqualTo(entry.Id));
        }

        [Test]
        public async Task DequeueAsync_WhenCancelled_ReturnsNull()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await _queue.DequeueAsync(cts.Token);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task DequeueAsync_WaitsForEnqueue_WhenQueueEmpty()
        {
            // Arrange
            var entry = new BackgroundTaskEntry();
            var dequeueTask = _queue.DequeueAsync(CancellationToken.None).AsTask();

            // Assert - Ensure the task hasn't completed yet
            Assert.That(dequeueTask.IsCompleted, Is.False);

            // Act
            await _queue.EnqueueAsync(entry);
            var dequeuedEntry = await dequeueTask;

            // Assert
            Assert.That(dequeuedEntry, Is.Not.Null);
            Assert.That(dequeuedEntry.Id, Is.EqualTo(entry.Id));
        }

        [Test]
        public async Task MultipleEnqueueAndDequeue_MaintainsOrder()
        {
            // Arrange
            var entry1 = new BackgroundTaskEntry();
            var entry2 = new BackgroundTaskEntry();

            await _queue.EnqueueAsync(entry1);
            await _queue.EnqueueAsync(entry2);

            // Act
            var dequeued1 = await _queue.DequeueAsync(CancellationToken.None);
            var dequeued2 = await _queue.DequeueAsync(CancellationToken.None);

            // Assert
            Assert.That(dequeued1, Is.Not.Null);
            Assert.That(dequeued1.Id, Is.EqualTo(entry1.Id));

            Assert.That(dequeued2, Is.Not.Null);
            Assert.That(dequeued2.Id, Is.EqualTo(entry2.Id));
        }

        [Test]
        public async Task Get_ReturnsExistingEntry()
        {
            // Arrange
            var entry = new BackgroundTaskEntry();
            await _queue.EnqueueAsync(entry);

            // Act
            var retrievedEntry = _queue.Get(entry.Id);

            // Assert
            Assert.That(retrievedEntry, Is.Not.Null);
            Assert.That(retrievedEntry.Id, Is.EqualTo(entry.Id));
        }

        [Test]
        public void Get_WhenNotExists_ReturnsNull()
        {
            // Arrange & Act
            var retrievedEntry = _queue.Get(Guid.NewGuid());

            // Assert
            Assert.That(retrievedEntry, Is.Null);
        }

        [Test]
        public async Task UpdateAsync_UpdatesExistingEntry()
        {
            // Arrange
            var entry = new BackgroundTaskEntry { Status = BackgroundTaskStatus.Queued };
            await _queue.EnqueueAsync(entry);

            // Act
            entry.Status = BackgroundTaskStatus.Processing;
            await _queue.UpdateAsync(entry);

            // Assert
            var retrievedEntry = _queue.Get(entry.Id);
            Assert.That(retrievedEntry, Is.Not.Null);
            Assert.That(retrievedEntry.Status, Is.EqualTo(BackgroundTaskStatus.Processing));
        }
    }
}
