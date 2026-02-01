using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests;

public class TaskHandlerTests
{
    [Fact]
    public async Task TimeTaskHandler_Returns_Current_Time()
    {
        // Arrange
        var handler = new TimeTaskHandler();

        // Act
        var result = await handler.HandleAsync("what time is it?");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("current time", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var handler = new TimeTaskHandler();

        // Assert
        Assert.Equal("time", handler.Name);
    }

    [Fact]
    public async Task ReminderTaskHandler_Saves_Reminder()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory h) => h);

        var handler = new ReminderTaskHandler(mockHistoryService.Object);

        // Act
        var result = await handler.HandleAsync("remind me to call mom");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("saved", result, StringComparison.OrdinalIgnoreCase);
        mockHistoryService.Verify(s => s.SaveRequestAsync(It.Is<RequestHistory>(
            h => h.RequestType == "reminder" && h.DetectedTask == "reminder"
        )), Times.Once);
    }

    [Fact]
    public void ReminderTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object);

        // Assert
        Assert.Equal("reminder", handler.Name);
    }
}
