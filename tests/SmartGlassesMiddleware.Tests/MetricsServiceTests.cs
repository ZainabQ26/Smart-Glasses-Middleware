using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

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
        public void IncrementErrorCount_NewEndpoint_ShouldSetErrorCountToOne()
        {
            // Arrange
            var endpoint = "/api/test";

            // Act
            _metricsService.IncrementErrorCount(endpoint);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary.ContainsKey(endpoint), Is.True);
            Assert.That(summary[endpoint].TotalErrors, Is.EqualTo(1));
        }

        [Test]
        public void IncrementErrorCount_ExistingEndpoint_ShouldIncrementErrorCount()
        {
            // Arrange
            var endpoint = "/api/test";
            _metricsService.IncrementErrorCount(endpoint); // Initial increment

            // Act
            _metricsService.IncrementErrorCount(endpoint);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary[endpoint].TotalErrors, Is.EqualTo(2));
        }

        [Test]
        public void IncrementErrorCount_MultipleEndpoints_ShouldTrackSeparately()
        {
            // Arrange
            var endpoint1 = "/api/test1";
            var endpoint2 = "/api/test2";

            // Act
            _metricsService.IncrementErrorCount(endpoint1);
            _metricsService.IncrementErrorCount(endpoint2);
            _metricsService.IncrementErrorCount(endpoint2);

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary[endpoint1].TotalErrors, Is.EqualTo(1));
            Assert.That(summary[endpoint2].TotalErrors, Is.EqualTo(2));
        }

        [Test]
        public void IncrementErrorCount_ConcurrentCalls_ShouldNotLoseIncrements()
        {
            // Arrange
            var endpoint = "/api/test";
            var numThreads = 100;

            // Act
            Parallel.For(0, numThreads, _ =>
            {
                _metricsService.IncrementErrorCount(endpoint);
            });

            // Assert
            var summary = _metricsService.GetSummary();
            Assert.That(summary[endpoint].TotalErrors, Is.EqualTo(numThreads));
        }
    }
}
