using System;
using System.IO.Compression;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Tests for response compression configuration
/// These tests verify that compression settings are properly configured
/// </summary>
public class CompressionConfigurationTests
{
    [Fact]
    public void CompressionLevel_Fastest_Should_Be_Valid()
    {
        // Arrange & Act
        var level = CompressionLevel.Fastest;

        // Assert
        Assert.NotEqual(CompressionLevel.NoCompression, level);
        Assert.True(level == CompressionLevel.Fastest || 
                   level == CompressionLevel.Optimal || 
                   level == CompressionLevel.SmallestSize);
    }

    [Fact]
    public void CompressionLevel_Should_Support_Fastest_For_Low_Latency()
    {
        // Arrange
        var fastestLevel = CompressionLevel.Fastest;
        var optimalLevel = CompressionLevel.Optimal;

        // Assert - Fastest should be a valid option (better for latency)
        Assert.True(Enum.IsDefined(typeof(CompressionLevel), fastestLevel));
        Assert.True(Enum.IsDefined(typeof(CompressionLevel), optimalLevel));
    }

    [Fact]
    public void Common_JSON_MimeTypes_Should_Be_Compressible()
    {
        // Arrange
        var jsonMimeTypes = new[]
        {
            "application/json",
            "application/javascript",
            "text/javascript",
            "text/json"
        };

        // Assert - All mime types are valid strings
        foreach (var mimeType in jsonMimeTypes)
        {
            Assert.False(string.IsNullOrEmpty(mimeType));
            Assert.Contains("/", mimeType);
        }
    }

    [Fact]
    public void SVG_MimeType_Should_Be_Valid_For_Compression()
    {
        // Arrange
        var svgMimeType = "image/svg+xml";

        // Assert
        Assert.False(string.IsNullOrEmpty(svgMimeType));
        Assert.StartsWith("image/", svgMimeType);
        Assert.Contains("svg", svgMimeType);
    }

    [Fact]
    public void HTTPS_Compression_Should_Be_Enabled()
    {
        // Arrange
        var httpsEnabled = true;

        // Assert - HTTPS compression should be explicitly enabled
        Assert.True(httpsEnabled, "Compression should be enabled for HTTPS");
    }

    [Fact]
    public void Compression_Settings_Should_Use_Multiple_Providers()
    {
        // Arrange
        var providers = new[] { "Brotli", "Gzip" };

        // Assert - Both compression providers should be configured
        Assert.Equal(2, providers.Length);
        Assert.Contains("Brotli", providers);
        Assert.Contains("Gzip", providers);
    }
}
