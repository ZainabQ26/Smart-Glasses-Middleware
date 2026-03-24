using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

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
        public async Task EnqueueAsync_AddsEntryToStoreAndQueueAndSignals()
        {
            // Arrange
            var entry = new BackgroundTaskEntry { Id = Guid.NewGuid() };
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            // Act
            await _queue.EnqueueAsync(entry);

            // Assert
            // 1. Verify it's in the store
            var storedEntry = _queue.Get(entry.Id);
            Assert.That(storedEntry, Is.Not.Null);
            Assert.That(storedEntry, Is.SameAs(entry));

            // 2. Verify it's in the queue (by dequeuing)
            var dequeuedEntry = await _queue.DequeueAsync(cancellationTokenSource.Token);
            Assert.That(dequeuedEntry, Is.Not.Null);
            Assert.That(dequeuedEntry, Is.SameAs(entry));
        }

        [Test]
        public async Task DequeueAsync_WithCancellation_ReturnsNull()
        {
            // Arrange
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel(); // Cancel immediately

            // Act
            var dequeuedEntry = await _queue.DequeueAsync(cancellationTokenSource.Token);

            // Assert
            Assert.That(dequeuedEntry, Is.Null);
        }

        [Test]
        public async Task DequeueAsync_WaitIsSignaled_ReturnsItem()
        {
            // Arrange
            var entry = new BackgroundTaskEntry { Id = Guid.NewGuid() };
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Enqueue in the background after a short delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                await _queue.EnqueueAsync(entry);
            });

            // Act - this will block until EnqueueAsync signals
            var dequeuedEntry = await _queue.DequeueAsync(cancellationTokenSource.Token);

            // Assert
            Assert.That(dequeuedEntry, Is.Not.Null);
            Assert.That(dequeuedEntry!.Id, Is.EqualTo(entry.Id));
        }

        [Test]
        public void Get_NonExistentId_ReturnsNull()
        {
            // Arrange
            var nonExistentId = Guid.NewGuid();

            // Act
            var result = _queue.Get(nonExistentId);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task UpdateAsync_UpdatesExistingEntryInStore()
        {
            // Arrange
            var entryId = Guid.NewGuid();
            var initialEntry = new BackgroundTaskEntry { Id = entryId, Status = BackgroundTaskStatus.Queued };
            await _queue.EnqueueAsync(initialEntry);

            // Create an updated entry with the same ID
            var updatedEntry = new BackgroundTaskEntry { Id = entryId, Status = BackgroundTaskStatus.Processing };

            // Act
            await _queue.UpdateAsync(updatedEntry);

            // Assert
            var retrievedEntry = _queue.Get(entryId);
            Assert.That(retrievedEntry, Is.Not.Null);
            Assert.That(retrievedEntry!.Status, Is.EqualTo(BackgroundTaskStatus.Processing));
            Assert.That(retrievedEntry, Is.SameAs(updatedEntry)); // In NoOp, it just overwrites the reference
        }
    }
}
