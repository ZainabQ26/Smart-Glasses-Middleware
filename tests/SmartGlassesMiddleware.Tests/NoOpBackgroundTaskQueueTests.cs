using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
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
        public async Task DequeueAsync_WithCanceledToken_ReturnsNull()
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
        public async Task EnqueueAndDequeue_NormalFlow_ReturnsItem()
        {
            // Arrange
            var entry = new BackgroundTaskEntry { Id = Guid.NewGuid() };
            await _queue.EnqueueAsync(entry);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // Timeout to prevent test hanging

            // Act
            var result = await _queue.DequeueAsync(cts.Token);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Id, Is.EqualTo(entry.Id));
        }

        [Test]
        public async Task Get_ExistingId_ReturnsItem()
        {
            // Arrange
            var entry = new BackgroundTaskEntry { Id = Guid.NewGuid() };
            await _queue.EnqueueAsync(entry);

            // Act
            var result = _queue.Get(entry.Id);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Id, Is.EqualTo(entry.Id));
        }

        [Test]
        public void Get_NonExistingId_ReturnsNull()
        {
            // Act
            var result = _queue.Get(Guid.NewGuid());

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task UpdateAsync_UpdatesItemInStore()
        {
            // Arrange
            var entry = new BackgroundTaskEntry { Id = Guid.NewGuid(), PromptText = "Initial" };
            await _queue.EnqueueAsync(entry);

            var updatedEntry = new BackgroundTaskEntry { Id = entry.Id, PromptText = "Updated" };

            // Act
            await _queue.UpdateAsync(updatedEntry);
            var result = _queue.Get(entry.Id);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.PromptText, Is.EqualTo("Updated"));
        }
    }
}
