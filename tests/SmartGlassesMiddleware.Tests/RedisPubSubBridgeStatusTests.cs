using System;
using Xunit;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class RedisPubSubBridgeStatusTests
{
    [Fact]
    public void RedisPubSubBridgeStatus_Default_Values()
    {
        // Arrange & Act
        var status = new RedisPubSubBridgeStatus();

        // Assert
        Assert.False(status.IsSubscribed);
        Assert.Null(status.LastError);
        Assert.Null(status.LastUpdatedUtc);
    }

    [Fact]
    public void RedisPubSubBridgeStatus_Can_Set_IsSubscribed()
    {
        // Arrange
        var status = new RedisPubSubBridgeStatus();

        // Act
        status.IsSubscribed = true;

        // Assert
        Assert.True(status.IsSubscribed);
    }

    [Fact]
    public void RedisPubSubBridgeStatus_Can_Set_LastError()
    {
        // Arrange
        var status = new RedisPubSubBridgeStatus();
        var errorMessage = "Test error";

        // Act
        status.LastError = errorMessage;

        // Assert
        Assert.Equal(errorMessage, status.LastError);
    }

    [Fact]
    public void RedisPubSubBridgeStatus_Can_Set_LastUpdatedUtc()
    {
        // Arrange
        var status = new RedisPubSubBridgeStatus();
        var timestamp = DateTime.UtcNow;

        // Act
        status.LastUpdatedUtc = timestamp;

        // Assert
        Assert.Equal(timestamp, status.LastUpdatedUtc);
    }
}
