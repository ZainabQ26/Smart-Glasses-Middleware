using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using System;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class MetricsServiceTests
    {
        private MetricsService _metricsService;

        [SetUp]
        public void Setup()
        {
            _metricsService = new MetricsService();
        }

        [Test]
        public void GetSummary_Empty_ReturnsEmptyDictionary()
        {
            // Act
            var summary = _metricsService.GetSummary();

            // Assert
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary, Is.Empty);
        }

        [Test]
        public void GetSummary_WithRequestCount_ReturnsCorrectSummary()
        {
            // Arrange
            var endpoint = "/api/test";

            // Act
            _metricsService.IncrementRequestCount(endpoint);
            _metricsService.IncrementRequestCount(endpoint);

            var summary = _metricsService.GetSummary();

            // Assert
            Assert.That(summary, Contains.Key(endpoint));
            var metrics = summary[endpoint];
            Assert.Multiple(() =>
            {
                Assert.That(metrics.TotalRequests, Is.EqualTo(2));
                Assert.That(metrics.TotalErrors, Is.EqualTo(0));
                Assert.That(metrics.AverageDurationMs, Is.EqualTo(0.0));
                Assert.That(metrics.LastRequestUtc, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
                Assert.That(metrics.LastRequestUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(1)));
            });
        }

        [Test]
        public void GetSummary_WithErrorCount_ReturnsCorrectSummary()
        {
            // Arrange
            var endpoint = "/api/error";

            // Act
            _metricsService.IncrementErrorCount(endpoint);

            var summary = _metricsService.GetSummary();

            // Assert
            Assert.That(summary, Contains.Key(endpoint));
            var metrics = summary[endpoint];
            Assert.Multiple(() =>
            {
                Assert.That(metrics.TotalRequests, Is.EqualTo(0));
                Assert.That(metrics.TotalErrors, Is.EqualTo(1));
                Assert.That(metrics.AverageDurationMs, Is.EqualTo(0.0));
            });
        }

        [Test]
        public void GetSummary_WithDuration_ReturnsCorrectSummary()
        {
            // Arrange
            var endpoint = "/api/duration";

            // Act
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(100));
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(200));
            _metricsService.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(150));

            var summary = _metricsService.GetSummary();

            // Assert
            Assert.That(summary, Contains.Key(endpoint));
            var metrics = summary[endpoint];
            Assert.Multiple(() =>
            {
                Assert.That(metrics.TotalRequests, Is.EqualTo(0)); // Only duration was recorded
                Assert.That(metrics.TotalErrors, Is.EqualTo(0));
                Assert.That(metrics.AverageDurationMs, Is.EqualTo(150.0).Within(0.001));
            });
        }

        [Test]
        public void GetSummary_MultipleEndpoints_ReturnsCorrectSummaries()
        {
            // Arrange
            var endpoint1 = "/api/one";
            var endpoint2 = "/api/two";

            // Act
            _metricsService.IncrementRequestCount(endpoint1);
            _metricsService.RecordRequestDuration(endpoint1, TimeSpan.FromMilliseconds(50));

            _metricsService.IncrementErrorCount(endpoint2);
            _metricsService.IncrementErrorCount(endpoint2);
            _metricsService.RecordRequestDuration(endpoint2, TimeSpan.FromMilliseconds(300));

            var summary = _metricsService.GetSummary();

            // Assert
            Assert.That(summary, Has.Count.EqualTo(2));

            Assert.That(summary, Contains.Key(endpoint1));
            Assert.Multiple(() =>
            {
                Assert.That(summary[endpoint1].TotalRequests, Is.EqualTo(1));
                Assert.That(summary[endpoint1].TotalErrors, Is.EqualTo(0));
                Assert.That(summary[endpoint1].AverageDurationMs, Is.EqualTo(50.0).Within(0.001));
            });

            Assert.That(summary, Contains.Key(endpoint2));
            Assert.Multiple(() =>
            {
                Assert.That(summary[endpoint2].TotalRequests, Is.EqualTo(0));
                Assert.That(summary[endpoint2].TotalErrors, Is.EqualTo(2));
                Assert.That(summary[endpoint2].AverageDurationMs, Is.EqualTo(300.0).Within(0.001));
            });
        }
    }
}
