using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class ServerSettingsServiceTests
{
    private Mock<ICacheService> _cacheMock;
    private Mock<ILogger<ServerSettingsService>> _loggerMock;
    private ServerSettingsService _service;

    [SetUp]
    public void Setup()
    {
        _cacheMock = new Mock<ICacheService>();
        _loggerMock = new Mock<ILogger<ServerSettingsService>>();
    }

    [Test]
    public async Task GetPreventSleepStateAsync_WhenBefore2Am_DoesNotReset()
    {
        // Arrange
        // Feb 15, 2024 at 1:00 AM Eastern Time
        var easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var testEasternTime = new DateTime(2024, 2, 15, 1, 0, 0, DateTimeKind.Unspecified);
        var testUtcTime = TimeZoneInfo.ConvertTimeToUtc(testEasternTime, easternTimeZone);

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testUtcTime));

        _cacheMock.Setup(c => c.GetAsync<bool?>("admin:setting:prevent-sleep:enabled"))
            .ReturnsAsync(true);
        _cacheMock.Setup(c => c.GetAsync<string>("admin:setting:prevent-sleep:last-reset-et-date"))
            .ReturnsAsync("2024-02-14"); // Reset yesterday

        _service = new ServerSettingsService(_cacheMock.Object, _loggerMock.Object, timeProviderMock.Object);

        // Act
        var result = await _service.GetPreventSleepStateAsync();

        // Assert
        Assert.That(result.Enabled, Is.True);
        Assert.That(result.AutoDisabledAt2AmEt, Is.False);

        // Ensure cache was not modified
        _cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Test]
    public async Task GetPreventSleepStateAsync_WhenAfter2Am_AndAlreadyResetToday_DoesNotReset()
    {
        // Arrange
        // Feb 15, 2024 at 3:00 AM Eastern Time
        var easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var testEasternTime = new DateTime(2024, 2, 15, 3, 0, 0, DateTimeKind.Unspecified);
        var testUtcTime = TimeZoneInfo.ConvertTimeToUtc(testEasternTime, easternTimeZone);

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testUtcTime));

        _cacheMock.Setup(c => c.GetAsync<bool?>("admin:setting:prevent-sleep:enabled"))
            .ReturnsAsync(true);
        _cacheMock.Setup(c => c.GetAsync<string>("admin:setting:prevent-sleep:last-reset-et-date"))
            .ReturnsAsync("2024-02-15"); // Reset today!

        _service = new ServerSettingsService(_cacheMock.Object, _loggerMock.Object, timeProviderMock.Object);

        // Act
        var result = await _service.GetPreventSleepStateAsync();

        // Assert
        Assert.That(result.Enabled, Is.True);
        Assert.That(result.AutoDisabledAt2AmEt, Is.False);

        // Ensure cache was not modified
        _cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Test]
    public async Task GetPreventSleepStateAsync_WhenAfter2Am_AndNotResetToday_Resets()
    {
        // Arrange
        // Feb 15, 2024 at 3:00 AM Eastern Time
        var easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var testEasternTime = new DateTime(2024, 2, 15, 3, 0, 0, DateTimeKind.Unspecified);
        var testUtcTime = TimeZoneInfo.ConvertTimeToUtc(testEasternTime, easternTimeZone);

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testUtcTime));

        _cacheMock.Setup(c => c.GetAsync<bool?>("admin:setting:prevent-sleep:enabled"))
            .ReturnsAsync(true);
        _cacheMock.Setup(c => c.GetAsync<string>("admin:setting:prevent-sleep:last-reset-et-date"))
            .ReturnsAsync("2024-02-14"); // Reset yesterday!

        _service = new ServerSettingsService(_cacheMock.Object, _loggerMock.Object, timeProviderMock.Object);

        // Act
        var result = await _service.GetPreventSleepStateAsync();

        // Assert
        Assert.That(result.Enabled, Is.False, "Expected Enabled to be false because it should have auto-disabled.");
        Assert.That(result.AutoDisabledAt2AmEt, Is.True);

        // Ensure cache was updated to false
        _cacheMock.Verify(c => c.SetAsync("admin:setting:prevent-sleep:enabled", false, It.IsAny<TimeSpan?>()), Times.Once);
        // Ensure reset date was updated to today
        _cacheMock.Verify(c => c.SetAsync("admin:setting:prevent-sleep:last-reset-et-date", "2024-02-15", It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Test]
    public async Task GetPreventSleepStateAsync_WhenNotEnabled_AndAfter2Am_UpdatesResetDateButDoesNotAutoDisable()
    {
        // Arrange
        // Feb 15, 2024 at 3:00 AM Eastern Time
        var easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var testEasternTime = new DateTime(2024, 2, 15, 3, 0, 0, DateTimeKind.Unspecified);
        var testUtcTime = TimeZoneInfo.ConvertTimeToUtc(testEasternTime, easternTimeZone);

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testUtcTime));

        _cacheMock.Setup(c => c.GetAsync<bool?>("admin:setting:prevent-sleep:enabled"))
            .ReturnsAsync(false); // Currently disabled
        _cacheMock.Setup(c => c.GetAsync<string>("admin:setting:prevent-sleep:last-reset-et-date"))
            .ReturnsAsync("2024-02-14"); // Reset yesterday!

        _service = new ServerSettingsService(_cacheMock.Object, _loggerMock.Object, timeProviderMock.Object);

        // Act
        var result = await _service.GetPreventSleepStateAsync();

        // Assert
        Assert.That(result.Enabled, Is.False);
        Assert.That(result.AutoDisabledAt2AmEt, Is.False, "Expected AutoDisabledAt2AmEt to be false because it was already disabled.");

        // Ensure cache for 'enabled' was NOT updated (since it was already false)
        _cacheMock.Verify(c => c.SetAsync("admin:setting:prevent-sleep:enabled", It.IsAny<bool>(), It.IsAny<TimeSpan?>()), Times.Never);
        // Ensure reset date WAS updated to today
        _cacheMock.Verify(c => c.SetAsync("admin:setting:prevent-sleep:last-reset-et-date", "2024-02-15", It.IsAny<TimeSpan?>()), Times.Once);
    }
}