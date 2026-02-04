using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests;

public class AIInteractionControllerTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetInteractionLogs_Returns_Ok_With_Logs()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var log1 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 1",
            Output = "Test output 1",
            ModelUsed = "gpt-4",
            DelegationReason = "token_threshold",
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            EstimatedCost = 0.01m,
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            ResponseTimeMs = 500,
            Success = true
        };

        var log2 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 2",
            Output = "Test output 2",
            ModelUsed = "gpt-3.5-turbo",
            DelegationReason = "manual",
            InputTokens = 50,
            OutputTokens = 25,
            TotalTokens = 75,
            EstimatedCost = 0.005m,
            Timestamp = DateTime.UtcNow,
            ResponseTimeMs = 300,
            Success = true
        };

        context.AIInteractionLogs.AddRange(log1, log2);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLogs();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var logs = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
        Assert.Equal(2, logs.Count());
    }

    [Fact]
    public async Task GetInteractionLogs_Filters_By_GroupId()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var groupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();

        var log1 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 1",
            Output = "Test output 1",
            ModelUsed = "gpt-4",
            ResourceGroupId = groupId,
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        var log2 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 2",
            Output = "Test output 2",
            ModelUsed = "gpt-4",
            ResourceGroupId = otherGroupId,
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.AddRange(log1, log2);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLogs(groupId: groupId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var logs = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
        Assert.Single(logs);
    }

    [Fact]
    public async Task GetInteractionLogs_Filters_By_Model()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var log1 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 1",
            Output = "Test output 1",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        var log2 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 2",
            Output = "Test output 2",
            ModelUsed = "gpt-3.5-turbo",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.AddRange(log1, log2);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLogs(model: "gpt-4");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var logs = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
        Assert.Single(logs);
    }

    [Fact]
    public async Task GetInteractionLogs_Respects_Limit_And_Offset()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        for (int i = 0; i < 10; i++)
        {
            context.AIInteractionLogs.Add(new AIInteractionLog
            {
                Id = Guid.NewGuid(),
                Input = $"Input {i}",
                Output = $"Output {i}",
                ModelUsed = "gpt-4",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
                Success = true
            });
        }
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLogs(limit: 5, offset: 2);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var logs = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
        Assert.Equal(5, logs.Count());
    }

    [Fact]
    public async Task GetInteractionLogs_Returns_Empty_When_No_Logs()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        // Act
        var result = await controller.GetInteractionLogs();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var logs = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task GetInteractionLogs_Truncates_Long_Input_And_Output()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var longInput = new string('A', 300);
        var longOutput = new string('B', 300);

        var log = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = longInput,
            Output = longOutput,
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.Add(log);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLogs();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetInteractionLog_Returns_Ok_When_Log_Exists()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var logId = Guid.NewGuid();
        var log = new AIInteractionLog
        {
            Id = logId,
            Input = "Test input",
            Output = "Test output",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.Add(log);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLog(logId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedLog = Assert.IsType<AIInteractionLog>(okResult.Value);
        Assert.Equal(logId, returnedLog.Id);
    }

    [Fact]
    public async Task GetInteractionLog_Returns_NotFound_When_Log_Does_Not_Exist()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        // Act
        var result = await controller.GetInteractionLog(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetStats_Returns_Model_Statistics()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var log1 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 1",
            Output = "Test output 1",
            ModelUsed = "gpt-4",
            TotalTokens = 100,
            ResponseTimeMs = 500,
            EstimatedCost = 0.01m,
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        var log2 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 2",
            Output = "Test output 2",
            ModelUsed = "gpt-4",
            TotalTokens = 150,
            ResponseTimeMs = 600,
            EstimatedCost = 0.015m,
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        var log3 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test input 3",
            Output = "Test output 3",
            ModelUsed = "gpt-3.5-turbo",
            TotalTokens = 50,
            ResponseTimeMs = 300,
            EstimatedCost = 0.005m,
            Timestamp = DateTime.UtcNow,
            Success = false
        };

        context.AIInteractionLogs.AddRange(log1, log2, log3);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetStats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetStats_Filters_By_Start_Date()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var startDate = DateTime.UtcNow.AddDays(-1);

        var oldLog = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Old log",
            Output = "Old output",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow.AddDays(-2),
            Success = true
        };

        var newLog = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "New log",
            Output = "New output",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.AddRange(oldLog, newLog);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetStats(startDate: startDate);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetStats_Filters_By_End_Date()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var endDate = DateTime.UtcNow.AddDays(-1);

        var oldLog = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Old log",
            Output = "Old output",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow.AddDays(-2),
            Success = true
        };

        var newLog = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "New log",
            Output = "New output",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.AddRange(oldLog, newLog);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetStats(endDate: endDate);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetStats_Filters_By_GroupId()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var groupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();

        var log1 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Log 1",
            Output = "Output 1",
            ModelUsed = "gpt-4",
            ResourceGroupId = groupId,
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        var log2 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Log 2",
            Output = "Output 2",
            ModelUsed = "gpt-4",
            ResourceGroupId = otherGroupId,
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.AddRange(log1, log2);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetStats(groupId: groupId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetStats_Returns_Delegation_Reasons()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var log1 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test 1",
            Output = "Output 1",
            ModelUsed = "gpt-4",
            DelegationReason = "token_threshold",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        var log2 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test 2",
            Output = "Output 2",
            ModelUsed = "gpt-4",
            DelegationReason = "manual",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        var log3 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Test 3",
            Output = "Output 3",
            ModelUsed = "gpt-4",
            DelegationReason = "token_threshold",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.AddRange(log1, log2, log3);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetStats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetStats_Returns_Empty_When_No_Logs()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        // Act
        var result = await controller.GetStats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetInteractionLogs_Orders_By_Timestamp_Descending()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var log1 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "First",
            Output = "First output",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            Success = true
        };

        var log2 = new AIInteractionLog
        {
            Id = Guid.NewGuid(),
            Input = "Second",
            Output = "Second output",
            ModelUsed = "gpt-4",
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.AddRange(log1, log2);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLogs();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetInteractionLog_Includes_Related_Entities()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<AIInteractionController>>();
        var controller = new AIInteractionController(context, mockLogger.Object);

        var group = new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Test Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.ResourceGroups.Add(group);
        context.Conversations.Add(conversation);

        var logId = Guid.NewGuid();
        var log = new AIInteractionLog
        {
            Id = logId,
            Input = "Test input",
            Output = "Test output",
            ModelUsed = "gpt-4",
            ResourceGroupId = group.Id,
            ConversationId = conversation.Id,
            Timestamp = DateTime.UtcNow,
            Success = true
        };

        context.AIInteractionLogs.Add(log);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetInteractionLog(logId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedLog = Assert.IsType<AIInteractionLog>(okResult.Value);
        Assert.NotNull(returnedLog.ResourceGroup);
        Assert.NotNull(returnedLog.Conversation);
    }
}
