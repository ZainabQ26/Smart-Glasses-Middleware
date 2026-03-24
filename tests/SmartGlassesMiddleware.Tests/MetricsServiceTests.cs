using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class MetricsServiceTests
    {
        private MetricsService _metricsService;

        [SetUp]
        public void SetUp()
        {
            _metricsService = new MetricsService();
        }

        [Test]
        public void RecordRequestDuration_SingleRecording_SetsAverageCorrectly()
        {
            // Arrange
            var endpoint = "/api/test";
            var duration = TimeSpan.FromMilliseconds(150);

            // Act
            _metricsService.RecordRequestDuration(endpoint, duration);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            Assert.That(summary[endpoint].AverageDurationMs, Is.EqualTo(150));
        }

        [Test]
        public void RecordRequestDuration_MultipleRecordings_CalculatesAverageCorrectly()
        {
            // Arrange
            var endpoint = "/api/test";

            // Act
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(100));
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(200));
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(150));

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            Assert.That(summary[endpoint].AverageDurationMs, Is.EqualTo(150));
        }

        [Test]
        public void RecordRequestDuration_MultipleEndpoints_IsolatesMetrics()
        {
            // Arrange
            var endpoint1 = "/api/test1";
            var endpoint2 = "/api/test2";

            // Act
            _metricsService.RecordRequestDuration(endpoint1, TimeSpan.FromMilliseconds(100));
            _metricsService.RecordRequestDuration(endpoint2, TimeSpan.FromMilliseconds(200));

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint1), Is.True);
            Assert.That(summary[endpoint1].AverageDurationMs, Is.EqualTo(100));
            Assert.That(summary.ContainsKey(endpoint2), Is.True);
            Assert.That(summary[endpoint2].AverageDurationMs, Is.EqualTo(200));
        }

        [Test]
        public void RecordRequestDuration_ZeroDuration_CalculatesAverageCorrectly()
        {
            // Arrange
            var endpoint = "/api/test";

            // Act
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(100));
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.Zero);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            Assert.That(summary[endpoint].AverageDurationMs, Is.EqualTo(50));
        }

        [Test]
        public async Task RecordRequestDuration_ConcurrentRecordings_MaintainsThreadSafety()
        {
            // Arrange
            var endpoint = "/api/test";
            int numTasks = 1000;
            var duration = TimeSpan.FromMilliseconds(10);

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < numTasks; i++)
            {
                tasks.Add(Task.Run(() => _metricsService.RecordRequestDuration(endpoint, duration)));
            }

            await Task.WhenAll(tasks);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            // Even with many concurrent requests, the average should remain exactly 10ms
            Assert.That(summary[endpoint].AverageDurationMs, Is.EqualTo(10).Within(0.0001));
        }
    }
}
