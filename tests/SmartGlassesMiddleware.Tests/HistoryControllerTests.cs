using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class HistoryControllerTests
{
    private HistoryController CreateController(
        Mock<IHistoryService>? historyService = null,
        Mock<IRAGService>? ragService = null)
    {
        var mockHistoryService = historyService ?? new Mock<IHistoryService>();
        var mockRagService = ragService ?? new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<HistoryController>>();

        var controller = new HistoryController(
            mockHistoryService.Object,
            mockRagService.Object,
            mockLogger.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    #region GetRecentHistory Tests

    [Fact]
    public async Task GetRecentHistory_Returns_Ok_With_History_List()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var histories = new List<RequestHistory>
        {
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "Audio", InputText = "Test 1" },
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "Text", InputText = "Test 2" }
        };

        mockHistoryService.Setup(s => s.GetRecentHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(histories);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetRecentHistory(10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedHistories = Assert.IsAssignableFrom<List<RequestHistory>>(okResult.Value);
        Assert.Equal(2, returnedHistories.Count);
    }

    [Fact]
    public async Task GetRecentHistory_Returns_NotFound_When_No_History()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetRecentHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<RequestHistory>());

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetRecentHistory(10);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(notFoundResult.Value);
    }

    [Fact]
    public async Task GetRecentHistory_Returns_NotFound_When_Null()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetRecentHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync((List<RequestHistory>)null!);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetRecentHistory(10);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(notFoundResult.Value);
    }

    [Fact]
    public async Task GetRecentHistory_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetRecentHistoryAsync(It.IsAny<int>()))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetRecentHistory(10);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    [Fact]
    public async Task GetRecentHistory_Uses_Provided_Count()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var histories = new List<RequestHistory>
        {
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "Audio" }
        };

        mockHistoryService.Setup(s => s.GetRecentHistoryAsync(25))
            .ReturnsAsync(histories);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetRecentHistory(25);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        mockHistoryService.Verify(s => s.GetRecentHistoryAsync(25), Times.Once);
    }

    #endregion

    #region GetHistoryById Tests

    [Fact]
    public async Task GetHistoryById_Returns_Ok_With_History()
    {
        // Arrange
        var historyId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        var history = new RequestHistory { Id = historyId, RequestType = "Audio", InputText = "Test" };

        mockHistoryService.Setup(s => s.GetHistoryByIdAsync(historyId))
            .ReturnsAsync(history);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetHistoryById(historyId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedHistory = Assert.IsType<RequestHistory>(okResult.Value);
        Assert.Equal(historyId, returnedHistory.Id);
    }

    [Fact]
    public async Task GetHistoryById_Returns_NotFound_When_History_Does_Not_Exist()
    {
        // Arrange
        var historyId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetHistoryByIdAsync(historyId))
            .ReturnsAsync((RequestHistory?)null);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetHistoryById(historyId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(notFoundResult.Value);
    }

    [Fact]
    public async Task GetHistoryById_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var historyId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetHistoryByIdAsync(historyId))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetHistoryById(historyId);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    #endregion

    #region GetReminders Tests

    [Fact]
    public async Task GetReminders_Returns_Ok_With_Reminders()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var reminders = new List<RequestHistory>
        {
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "Call John" },
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "Meeting at 3" }
        };

        mockHistoryService.Setup(s => s.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
            .ReturnsAsync(reminders);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetReminders(100);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedReminders = Assert.IsAssignableFrom<List<RequestHistory>>(okResult.Value);
        Assert.Equal(2, returnedReminders.Count);
    }

    [Fact]
    public async Task GetReminders_Returns_Empty_Array_When_No_Reminders()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
            .ReturnsAsync(new List<RequestHistory>());

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetReminders(100);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedReminders = Assert.IsAssignableFrom<List<RequestHistory>>(okResult.Value);
        Assert.Empty(returnedReminders);
    }

    [Fact]
    public async Task GetReminders_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetReminders(100);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    #endregion

    #region GetHistoryByType Tests

    [Fact]
    public async Task GetHistoryByType_Returns_Ok_With_History()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var histories = new List<RequestHistory>
        {
            new RequestHistory { Id = Guid.NewGuid(), RequestType = "custom", InputText = "Test" }
        };

        mockHistoryService.Setup(s => s.GetHistoryByTypeAsync("custom", It.IsAny<int>()))
            .ReturnsAsync(histories);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetHistoryByType("custom", 100);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedHistories = Assert.IsAssignableFrom<List<RequestHistory>>(okResult.Value);
        Assert.Single(returnedHistories);
    }

    [Fact]
    public async Task GetHistoryByType_Returns_Empty_Array_When_No_History()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetHistoryByTypeAsync("nonexistent", It.IsAny<int>()))
            .ReturnsAsync(new List<RequestHistory>());

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetHistoryByType("nonexistent", 100);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedHistories = Assert.IsAssignableFrom<List<RequestHistory>>(okResult.Value);
        Assert.Empty(returnedHistories);
    }

    [Fact]
    public async Task GetHistoryByType_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.GetHistoryByTypeAsync("custom", It.IsAny<int>()))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.GetHistoryByType("custom", 100);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    #endregion

    #region UpdateReminder Tests

    [Fact]
    public async Task UpdateReminder_Returns_Ok_With_Updated_Reminder()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        var updatedReminder = new RequestHistory
        {
            Id = reminderId,
            RequestType = "reminder",
            InputText = "Updated reminder text"
        };

        mockHistoryService.Setup(s => s.UpdateReminderAsync(reminderId, "Updated reminder text"))
            .ReturnsAsync(updatedReminder);

        var controller = CreateController(historyService: mockHistoryService);
        var request = new UpdateReminderRequest { Text = "Updated reminder text" };

        // Act
        var result = await controller.UpdateReminder(reminderId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedReminder = Assert.IsType<RequestHistory>(okResult.Value);
        Assert.Equal(reminderId, returnedReminder.Id);
        Assert.Equal("Updated reminder text", returnedReminder.InputText);
    }

    [Fact]
    public async Task UpdateReminder_Returns_BadRequest_When_Request_Is_Null()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var controller = CreateController();

        // Act
        var result = await controller.UpdateReminder(reminderId, null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateReminder_Returns_BadRequest_When_Text_Is_Empty()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var controller = CreateController();
        var request = new UpdateReminderRequest { Text = "" };

        // Act
        var result = await controller.UpdateReminder(reminderId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateReminder_Returns_BadRequest_When_Text_Is_Whitespace()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var controller = CreateController();
        var request = new UpdateReminderRequest { Text = "   " };

        // Act
        var result = await controller.UpdateReminder(reminderId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateReminder_Returns_NotFound_When_Reminder_Does_Not_Exist()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.UpdateReminderAsync(reminderId, It.IsAny<string>()))
            .ReturnsAsync((RequestHistory?)null);

        var controller = CreateController(historyService: mockHistoryService);
        var request = new UpdateReminderRequest { Text = "Updated text" };

        // Act
        var result = await controller.UpdateReminder(reminderId, request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(notFoundResult.Value);
    }

    [Fact]
    public async Task UpdateReminder_Returns_BadRequest_On_ArgumentException()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.UpdateReminderAsync(reminderId, It.IsAny<string>()))
            .ThrowsAsync(new ArgumentException("Invalid argument"));

        var controller = CreateController(historyService: mockHistoryService);
        var request = new UpdateReminderRequest { Text = "Updated text" };

        // Act
        var result = await controller.UpdateReminder(reminderId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateReminder_Returns_Forbidden_On_InvalidOperationException()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.UpdateReminderAsync(reminderId, It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Cannot update this reminder"));

        var controller = CreateController(historyService: mockHistoryService);
        var request = new UpdateReminderRequest { Text = "Updated text" };

        // Act
        var result = await controller.UpdateReminder(reminderId, request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    [Fact]
    public async Task UpdateReminder_Returns_InternalServerError_On_Generic_Exception()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.UpdateReminderAsync(reminderId, It.IsAny<string>()))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(historyService: mockHistoryService);
        var request = new UpdateReminderRequest { Text = "Updated text" };

        // Act
        var result = await controller.UpdateReminder(reminderId, request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    #endregion

    #region DeleteReminder Tests

    [Fact]
    public async Task DeleteReminder_Returns_NoContent_When_Successful()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.DeleteReminderAsync(reminderId))
            .ReturnsAsync(true);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.DeleteReminder(reminderId);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteReminder_Returns_NotFound_When_Reminder_Does_Not_Exist()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.DeleteReminderAsync(reminderId))
            .ReturnsAsync(false);

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.DeleteReminder(reminderId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.IsType<ProblemDetails>(notFoundResult.Value);
    }

    [Fact]
    public async Task DeleteReminder_Returns_Forbidden_On_InvalidOperationException()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.DeleteReminderAsync(reminderId))
            .ThrowsAsync(new InvalidOperationException("Cannot delete this reminder"));

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.DeleteReminder(reminderId);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    [Fact]
    public async Task DeleteReminder_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        var mockHistoryService = new Mock<IHistoryService>();
        mockHistoryService.Setup(s => s.DeleteReminderAsync(reminderId))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(historyService: mockHistoryService);

        // Act
        var result = await controller.DeleteReminder(reminderId);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    #endregion

    #region QueryReminders Tests

    [Fact]
    public async Task QueryReminders_Returns_Ok_With_Query_Result()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        var queryResult = new QueryResult
        {
            Answer = "Found 2 reminders",
            Sources = new List<DocumentSource>()
        };

        mockRagService.Setup(s => s.QueryRemindersAsync("meeting", 10))
            .ReturnsAsync(queryResult);

        var controller = CreateController(ragService: mockRagService);
        var request = new QueryRequest { Query = "meeting", TopK = 10 };

        // Act
        var result = await controller.QueryReminders(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedResult = Assert.IsType<QueryResult>(okResult.Value);
        Assert.Equal("Found 2 reminders", returnedResult.Answer);
    }

    [Fact]
    public async Task QueryReminders_Returns_BadRequest_When_Request_Is_Null()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.QueryReminders(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task QueryReminders_Returns_BadRequest_When_Query_Is_Empty()
    {
        // Arrange
        var controller = CreateController();
        var request = new QueryRequest { Query = "" };

        // Act
        var result = await controller.QueryReminders(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task QueryReminders_Returns_BadRequest_When_Query_Is_Whitespace()
    {
        // Arrange
        var controller = CreateController();
        var request = new QueryRequest { Query = "   " };

        // Act
        var result = await controller.QueryReminders(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task QueryReminders_Uses_Default_TopK_When_Not_Provided()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        var queryResult = new QueryResult { Answer = "Results" };

        mockRagService.Setup(s => s.QueryRemindersAsync("test", 10))
            .ReturnsAsync(queryResult);

        var controller = CreateController(ragService: mockRagService);
        var request = new QueryRequest { Query = "test" };

        // Act
        var result = await controller.QueryReminders(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        mockRagService.Verify(s => s.QueryRemindersAsync("test", 10), Times.Once);
    }

    [Fact]
    public async Task QueryReminders_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        mockRagService.Setup(s => s.QueryRemindersAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("Service error"));

        var controller = CreateController(ragService: mockRagService);
        var request = new QueryRequest { Query = "test" };

        // Act
        var result = await controller.QueryReminders(request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    #endregion
}
