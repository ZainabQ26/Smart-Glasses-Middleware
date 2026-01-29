using System.Collections.Concurrent;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Service for tracking application metrics and statistics
/// </summary>
public interface IMetricsService
{
    void IncrementRequestCount(string endpoint);
    void IncrementErrorCount(string endpoint);
    void RecordRequestDuration(string endpoint, TimeSpan duration);
    Dictionary<string, MetricsSummary> GetSummary();
}

public class MetricsSummary
{
    public long TotalRequests { get; set; }
    public long TotalErrors { get; set; }
    public double AverageDurationMs { get; set; }
    public DateTime LastRequestUtc { get; set; }
}

public class MetricsService : IMetricsService
{
    private readonly ConcurrentDictionary<string, EndpointMetrics> _metrics = new();

    public void IncrementRequestCount(string endpoint)
    {
        var metrics = _metrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
        lock (metrics)
        {
            Interlocked.Increment(ref metrics.RequestCount);
            metrics.LastRequestUtc = DateTime.UtcNow;
        }
    }

    public void IncrementErrorCount(string endpoint)
    {
        var metrics = _metrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
        lock (metrics)
        {
            Interlocked.Increment(ref metrics.ErrorCount);
        }
    }

    public void RecordRequestDuration(string endpoint, TimeSpan duration)
    {
        var metrics = _metrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
        
        lock (metrics)
        {
            // Update average duration using cumulative moving average
            var totalDuration = metrics.AverageDurationMs * metrics.RequestCount + duration.TotalMilliseconds;
            var newCount = metrics.RequestCount;
            if (newCount > 0)
            {
                metrics.AverageDurationMs = totalDuration / newCount;
            }
        }
    }

    public Dictionary<string, MetricsSummary> GetSummary()
    {
        return _metrics.ToDictionary(
            kvp => kvp.Key,
            kvp => new MetricsSummary
            {
                TotalRequests = kvp.Value.RequestCount,
                TotalErrors = kvp.Value.ErrorCount,
                AverageDurationMs = kvp.Value.AverageDurationMs,
                LastRequestUtc = kvp.Value.LastRequestUtc
            });
    }

    private class EndpointMetrics
    {
        public long RequestCount;
        public long ErrorCount;
        public double AverageDurationMs;
        public DateTime LastRequestUtc = DateTime.UtcNow;
    }
}
