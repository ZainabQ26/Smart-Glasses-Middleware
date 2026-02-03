using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
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

        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

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
        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Assert
        Assert.Equal("reminder", handler.Name);
    }

    [Fact]
    public async Task ReminderTaskHandler_Handles_Empty_Input()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("didn't catch", result, StringComparison.OrdinalIgnoreCase);
        mockHistoryService.Verify(s => s.SaveRequestAsync(It.IsAny<RequestHistory>()), Times.Never);
    }

    [Fact]
    public async Task ReminderTaskHandler_Handles_Whitespace_Input()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("   ");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("didn't catch", result, StringComparison.OrdinalIgnoreCase);
        mockHistoryService.Verify(s => s.SaveRequestAsync(It.IsAny<RequestHistory>()), Times.Never);
    }

    [Fact]
    public async Task ReminderTaskHandler_Handles_Null_Input()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("didn't catch", result, StringComparison.OrdinalIgnoreCase);
        mockHistoryService.Verify(s => s.SaveRequestAsync(It.IsAny<RequestHistory>()), Times.Never);
    }

    [Fact]
    public async Task ReminderTaskHandler_Handles_Long_Input()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        var longInput = new string('a', 1001); // 1001 characters

        // Act
        var result = await handler.HandleAsync(longInput);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("too long", result, StringComparison.OrdinalIgnoreCase);
        mockHistoryService.Verify(s => s.SaveRequestAsync(It.IsAny<RequestHistory>()), Times.Never);
    }

    [Fact]
    public async Task ReminderTaskHandler_Trims_Input()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory h) => h);

        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("  remind me to call mom  ");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("saved", result, StringComparison.OrdinalIgnoreCase);
        mockHistoryService.Verify(s => s.SaveRequestAsync(It.Is<RequestHistory>(
            h => h.InputText == "remind me to call mom" // Should be trimmed
        )), Times.Once);
    }

    [Fact]
    public async Task ReminderTaskHandler_Handles_SaveRequest_Exception()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ThrowsAsync(new Exception("Database error"));

        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
        var handler = new ReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("remind me to call mom");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("couldn't save", result, StringComparison.OrdinalIgnoreCase);
    }
}
