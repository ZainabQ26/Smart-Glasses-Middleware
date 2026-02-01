using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class MetricsServiceTests
{
    [Fact]
    public void IncrementRequestCount_Increments_Request_Count()
    {
        // Arrange
        var service = new MetricsService();
        var endpoint = "/api/test";

        // Act
        service.IncrementRequestCount(endpoint);
        service.IncrementRequestCount(endpoint);

        // Assert
        var summary = service.GetSummary();
        Assert.True(summary.ContainsKey(endpoint));
        Assert.Equal(2, summary[endpoint].TotalRequests);
    }

    [Fact]
    public void IncrementErrorCount_Increments_Error_Count()
    {
        // Arrange
        var service = new MetricsService();
        var endpoint = "/api/test";

        // Act
        service.IncrementRequestCount(endpoint);
        service.IncrementErrorCount(endpoint);

        // Assert
        var summary = service.GetSummary();
        Assert.Equal(1, summary[endpoint].TotalErrors);
    }

    [Fact]
    public void RecordRequestDuration_Updates_Average_Duration()
    {
        // Arrange
        var service = new MetricsService();
        var endpoint = "/api/test";

        // Act
        service.IncrementRequestCount(endpoint);
        service.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(100));
        service.IncrementRequestCount(endpoint);
        service.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(200));
        service.IncrementRequestCount(endpoint);
        service.RecordRequestDuration(endpoint, TimeSpan.FromMilliseconds(300));

        // Assert
        var summary = service.GetSummary();
        // Average of 100, 200, 300 = 600/3 = 200
        Assert.Equal(200.0, summary[endpoint].AverageDurationMs);
    }

    [Fact]
    public void GetSummary_Returns_Empty_Dictionary_When_No_Metrics()
    {
        // Arrange
        var service = new MetricsService();

        // Act
        var summary = service.GetSummary();

        // Assert
        Assert.NotNull(summary);
        Assert.Empty(summary);
    }

    [Fact]
    public void Metrics_Track_Multiple_Endpoints()
    {
        // Arrange
        var service = new MetricsService();

        // Act
        service.IncrementRequestCount("/api/endpoint1");
        service.IncrementRequestCount("/api/endpoint2");

        // Assert
        var summary = service.GetSummary();
        Assert.Equal(2, summary.Count);
        Assert.True(summary.ContainsKey("/api/endpoint1"));
        Assert.True(summary.ContainsKey("/api/endpoint2"));
    }
}
