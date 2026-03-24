using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

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
    public void IncrementErrorCount_NewEndpoint_IncrementsToOne()
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
    public void IncrementErrorCount_ExistingEndpoint_IncrementsByOne()
    {
        // Arrange
        var endpoint = "/api/test";
        _metricsService.IncrementErrorCount(endpoint);

        // Act
        _metricsService.IncrementErrorCount(endpoint);

        // Assert
        var summary = _metricsService.GetSummary();
        Assert.That(summary.ContainsKey(endpoint), Is.True);
        Assert.That(summary[endpoint].TotalErrors, Is.EqualTo(2));
    }

    [Test]
    public void IncrementErrorCount_MultipleEndpoints_TracksIndependently()
    {
        // Arrange
        var endpoint1 = "/api/test1";
        var endpoint2 = "/api/test2";

        // Act
        _metricsService.IncrementErrorCount(endpoint1);
        _metricsService.IncrementErrorCount(endpoint1);
        _metricsService.IncrementErrorCount(endpoint2);

        // Assert
        var summary = _metricsService.GetSummary();
        Assert.That(summary.ContainsKey(endpoint1), Is.True);
        Assert.That(summary[endpoint1].TotalErrors, Is.EqualTo(2));
        Assert.That(summary.ContainsKey(endpoint2), Is.True);
        Assert.That(summary[endpoint2].TotalErrors, Is.EqualTo(1));
    }

    [Test]
    public void IncrementErrorCount_ConcurrentCalls_IncrementsCorrectly()
    {
        // Arrange
        var endpoint = "/api/test";
        var concurrentCalls = 1000;

        // Act
        Parallel.For(0, concurrentCalls, _ =>
        {
            _metricsService.IncrementErrorCount(endpoint);
        });

        // Assert
        var summary = _metricsService.GetSummary();
        Assert.That(summary.ContainsKey(endpoint), Is.True);
        Assert.That(summary[endpoint].TotalErrors, Is.EqualTo(concurrentCalls));
    }
}
