using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System.Collections.Generic;
using System.Linq;

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

    // Edit Reminder Handler Tests

    [Fact]
    public async Task EditReminderTaskHandler_Edits_Reminder_Successfully()
    {
        // Arrange
        var existingReminder = new RequestHistory
        {
            Id = Guid.NewGuid(),
            RequestType = "reminder",
            InputText = "Buy milk",
            Timestamp = DateTime.UtcNow.AddHours(-1)
        };

        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.GetHistoryByTypeAsync("reminder", 100))
            .ReturnsAsync(new List<RequestHistory> { existingReminder });

        mockHistoryService
            .Setup(s => s.UpdateReminderAsync(existingReminder.Id, "Buy eggs"))
            .ReturnsAsync(new RequestHistory
            {
                Id = existingReminder.Id,
                RequestType = "reminder",
                InputText = "Buy eggs",
                Timestamp = existingReminder.Timestamp
            });

        var mockLogger = new Mock<ILogger<EditReminderTaskHandler>>();
        var handler = new EditReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("change milk to Buy eggs");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("updated", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Buy eggs", result);
        mockHistoryService.Verify(s => s.UpdateReminderAsync(existingReminder.Id, "Buy eggs"), Times.Once);
    }

    [Fact]
    public async Task EditReminderTaskHandler_Handles_No_Match()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.GetHistoryByTypeAsync("reminder", 100))
            .ReturnsAsync(new List<RequestHistory>());

        var mockLogger = new Mock<ILogger<EditReminderTaskHandler>>();
        var handler = new EditReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("change xyz to abc");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("couldn't find", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditReminderTaskHandler_Handles_Multiple_Matches()
    {
        // Arrange
        var reminders = new List<RequestHistory>
        {
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "Buy milk", Timestamp = DateTime.UtcNow.AddHours(-1) },
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "Buy milk and eggs", Timestamp = DateTime.UtcNow.AddHours(-2) }
        };

        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.GetHistoryByTypeAsync("reminder", 100))
            .ReturnsAsync(reminders);

        var mockLogger = new Mock<ILogger<EditReminderTaskHandler>>();
        var handler = new EditReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("change milk to bread");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("found", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", result);
    }

    [Fact]
    public async Task EditReminderTaskHandler_Handles_Empty_Input()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<EditReminderTaskHandler>>();
        var handler = new EditReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("didn't catch", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditReminderTaskHandler_Handles_Invalid_Format()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<EditReminderTaskHandler>>();
        var handler = new EditReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("just some random text");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("couldn't understand", result, StringComparison.OrdinalIgnoreCase);
    }

    // Delete Reminder Handler Tests

    [Fact]
    public async Task DeleteReminderTaskHandler_Deletes_Reminder_Successfully()
    {
        // Arrange
        var existingReminder = new RequestHistory
        {
            Id = Guid.NewGuid(),
            RequestType = "reminder",
            InputText = "Buy milk",
            Timestamp = DateTime.UtcNow.AddHours(-1)
        };

        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.GetHistoryByTypeAsync("reminder", 100))
            .ReturnsAsync(new List<RequestHistory> { existingReminder });

        mockHistoryService
            .Setup(s => s.DeleteReminderAsync(existingReminder.Id))
            .ReturnsAsync(true);

        var mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
        var handler = new DeleteReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("delete milk");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("deleted", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Buy milk", result);
        mockHistoryService.Verify(s => s.DeleteReminderAsync(existingReminder.Id), Times.Once);
    }

    [Fact]
    public async Task DeleteReminderTaskHandler_Handles_No_Match()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.GetHistoryByTypeAsync("reminder", 100))
            .ReturnsAsync(new List<RequestHistory>());

        var mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
        var handler = new DeleteReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("delete xyz");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("couldn't find", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteReminderTaskHandler_Handles_Multiple_Matches()
    {
        // Arrange
        var reminders = new List<RequestHistory>
        {
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "Buy milk", Timestamp = DateTime.UtcNow.AddHours(-1) },
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "Buy milk and eggs", Timestamp = DateTime.UtcNow.AddHours(-2) }
        };

        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.GetHistoryByTypeAsync("reminder", 100))
            .ReturnsAsync(reminders);

        var mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
        var handler = new DeleteReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("delete milk");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("found", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", result);
    }

    [Fact]
    public async Task DeleteReminderTaskHandler_Handles_Empty_Input()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
        var handler = new DeleteReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("didn't catch", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteReminderTaskHandler_Prevents_Delete_All()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService
            .Setup(s => s.GetHistoryByTypeAsync("reminder", 100))
            .ReturnsAsync(new List<RequestHistory>());

        var mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
        var handler = new DeleteReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("delete all reminders");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("can't delete all", result, StringComparison.OrdinalIgnoreCase);
    }
}
