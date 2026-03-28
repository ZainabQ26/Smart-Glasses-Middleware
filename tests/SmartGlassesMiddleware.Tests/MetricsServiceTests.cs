using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class MetricsServiceTests
{
    private MetricsService _service;

    [SetUp]
    public void Setup()
    {
        _service = new MetricsService();
    }

    [Test]
    public void IncrementRequestCount_Works()
    {
        _service.IncrementRequestCount("test");
        var summary = _service.GetSummary();
        Assert.That(summary.ContainsKey("test"), Is.True);
        Assert.That(summary["test"].TotalRequests, Is.EqualTo(1));
    }

    [Test]
    public void IncrementErrorCount_Works()
    {
        _service.IncrementErrorCount("test");
        var summary = _service.GetSummary();
        Assert.That(summary.ContainsKey("test"), Is.True);
        Assert.That(summary["test"].TotalErrors, Is.EqualTo(1));
    }

    [Test]
    public void RecordRequestDuration_Works()
    {
        _service.RecordRequestDuration("test", TimeSpan.FromMilliseconds(100));
        var summary = _service.GetSummary();
        Assert.That(summary.ContainsKey("test"), Is.True);
        Assert.That(summary["test"].AverageDurationMs, Is.EqualTo(100));

        _service.RecordRequestDuration("test", TimeSpan.FromMilliseconds(200));
        summary = _service.GetSummary();
        Assert.That(summary["test"].AverageDurationMs, Is.EqualTo(150));
    }
}
