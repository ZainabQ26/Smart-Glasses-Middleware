using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class ServerSettingsServiceTests
    {
        private Mock<ICacheService> _mockCache;
        private Mock<ILogger<ServerSettingsService>> _mockLogger;
        private ServerSettingsService _service;

        [SetUp]
        public void Setup()
        {
            _mockCache = new Mock<ICacheService>();
            _mockLogger = new Mock<ILogger<ServerSettingsService>>();
            _service = new ServerSettingsService(_mockCache.Object, _mockLogger.Object);
        }

        [Test]
        public async Task SetPreventSleepEnabledAsync_SetsTrue_PersistsValue()
        {
            // Arrange
            _mockCache.Setup(c => c.GetAsync<bool?>("admin:setting:prevent-sleep:enabled"))
                .ReturnsAsync((bool?)null);
            _mockCache.Setup(c => c.SetAsync("admin:setting:prevent-sleep:enabled", true, It.IsAny<TimeSpan?>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SetPreventSleepEnabledAsync(true, CancellationToken.None);

            // Assert
            Assert.That(result.Enabled, Is.True);
            _mockCache.Verify(c => c.SetAsync("admin:setting:prevent-sleep:enabled", true, It.IsAny<TimeSpan?>()), Times.Once);
        }

        [Test]
        public async Task SetPreventSleepEnabledAsync_SetsFalse_PersistsValue()
        {
            // Arrange
            _mockCache.Setup(c => c.GetAsync<bool?>("admin:setting:prevent-sleep:enabled"))
                .ReturnsAsync((bool?)null);
            _mockCache.Setup(c => c.SetAsync("admin:setting:prevent-sleep:enabled", false, It.IsAny<TimeSpan?>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SetPreventSleepEnabledAsync(false, CancellationToken.None);

            // Assert
            Assert.That(result.Enabled, Is.False);
            _mockCache.Verify(c => c.SetAsync("admin:setting:prevent-sleep:enabled", false, It.IsAny<TimeSpan?>()), Times.Once);
        }

        [Test]
        public async Task SetPreventSleepEnabledAsync_AlwaysAppliesDesiredValue_EvenIfAutoDisableTriggered()
        {
            // Arrange
            // We force a scenario where auto-disable might happen by setting existing value to true.
            _mockCache.Setup(c => c.GetAsync<bool?>("admin:setting:prevent-sleep:enabled"))
                .ReturnsAsync(true);
            _mockCache.Setup(c => c.GetAsync<string>("admin:setting:prevent-sleep:last-reset-et-date"))
                .ReturnsAsync("1999-01-01"); // old date to trigger auto-disable if hour >= 2

            // Act
            var result = await _service.SetPreventSleepEnabledAsync(true, CancellationToken.None);

            // Assert
            Assert.That(result.Enabled, Is.True); // Because the desired value persists.

            // Depending on the current time (Hour >= 2), auto disable might have triggered and set cache to false,
            // but the persistDesiredValue logic will eventually set it to true again.
            // We just verify that the last call to SetAsync for the enabled key sets it to true, or we check result.
            // Specifically, we should verify it was called at least once with true.
            _mockCache.Verify(c => c.SetAsync("admin:setting:prevent-sleep:enabled", true, It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
        }
    }
}
