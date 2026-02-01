using System;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class TasksControllerTests
{
    [Fact]
    public void GetTask_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        // Arrange
        var mockQueue = new Mock<IBackgroundTaskQueue>();
        mockQueue.Setup(q => q.Get(It.IsAny<Guid>())).Returns((BackgroundTaskEntry?)null);

        var controller = new TasksController(mockQueue.Object);
        var taskId = Guid.NewGuid();

        // Act
        var result = controller.GetTask(taskId);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetTask_Returns_Ok_When_Task_Is_Completed()
    {
        // Arrange
        var mockQueue = new Mock<IBackgroundTaskQueue>();
        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            Status = BackgroundTaskStatus.Completed,
            Result = "Success"
        };
        mockQueue.Setup(q => q.Get(taskId)).Returns(entry);

        var controller = new TasksController(mockQueue.Object);

        // Act
        var result = controller.GetTask(taskId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void GetTask_Returns_InternalServerError_When_Task_Failed()
    {
        // Arrange
        var mockQueue = new Mock<IBackgroundTaskQueue>();
        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            Status = BackgroundTaskStatus.Failed,
            Error = "An error occurred"
        };
        mockQueue.Setup(q => q.Get(taskId)).Returns(entry);

        var controller = new TasksController(mockQueue.Object);

        // Act
        var result = controller.GetTask(taskId);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public void GetTask_Returns_Accepted_When_Task_Is_Processing()
    {
        // Arrange
        var mockQueue = new Mock<IBackgroundTaskQueue>();
        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            Status = BackgroundTaskStatus.Processing
        };
        mockQueue.Setup(q => q.Get(taskId)).Returns(entry);

        var controller = new TasksController(mockQueue.Object);

        // Act
        var result = controller.GetTask(taskId);

        // Assert
        var acceptedResult = Assert.IsType<AcceptedResult>(result);
        Assert.Equal($"/api/tasks/{taskId}", acceptedResult.Location);
    }

    [Fact]
    public void GetTask_Returns_Accepted_When_Task_Is_Queued()
    {
        // Arrange
        var mockQueue = new Mock<IBackgroundTaskQueue>();
        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            Status = BackgroundTaskStatus.Queued
        };
        mockQueue.Setup(q => q.Get(taskId)).Returns(entry);

        var controller = new TasksController(mockQueue.Object);

        // Act
        var result = controller.GetTask(taskId);

        // Assert
        Assert.IsType<AcceptedResult>(result);
    }
}
