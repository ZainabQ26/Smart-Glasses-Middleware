using System;
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
        public void Setup()
        {
            _metricsService = new MetricsService();
        }

        [Test]
        public void IncrementRequestCount_AddsNewEndpoint_WhenEndpointIsNew()
        {
            // Arrange
            string endpoint = "/api/test";

            // Act
            _metricsService.IncrementRequestCount(endpoint);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            Assert.That(summary[endpoint].TotalRequests, Is.EqualTo(1));
            // Ensure that LastRequestUtc was updated recently
            Assert.That(summary[endpoint].LastRequestUtc, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void IncrementRequestCount_IncrementsExistingEndpoint_WhenEndpointExists()
        {
            // Arrange
            string endpoint = "/api/test";

            // Act
            _metricsService.IncrementRequestCount(endpoint);
            _metricsService.IncrementRequestCount(endpoint);
            _metricsService.IncrementRequestCount(endpoint);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            Assert.That(summary[endpoint].TotalRequests, Is.EqualTo(3));
        }

        [Test]
        public void IncrementRequestCount_IsThreadSafe()
        {
            // Arrange
            string endpoint = "/api/concurrent-test";
            int numberOfThreads = 100;
            int callsPerThread = 1000;
            int totalExpectedCalls = numberOfThreads * callsPerThread;

            // Act
            Parallel.For(0, numberOfThreads, _ =>
            {
                for (int i = 0; i < callsPerThread; i++)
                {
                    _metricsService.IncrementRequestCount(endpoint);
                }
            });

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            Assert.That(summary[endpoint].TotalRequests, Is.EqualTo(totalExpectedCalls));
        }
    }
}
