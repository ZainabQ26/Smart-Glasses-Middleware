using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

public class HistoryServiceEnhancedTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<HistoryService>> _mockLogger;
    private readonly HistoryService _service;

    public HistoryServiceEnhancedTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _mockLogger = new Mock<ILogger<HistoryService>>();
        _service = new HistoryService(_context, _mockLogger.Object);
    }

    [Fact]
    public async Task SaveRequestAsync_WithAllFields_ShouldSaveSuccessfully()
    {
        // Arrange
        var history = new RequestHistory
        {
            RequestType = "test",
            InputText = "input text",
            DetectedTask = "task",
            Response = "response text",
            UsedAdvancedModel = true,
            MediaStorageKey = "storage-key",
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = await _service.SaveRequestAsync(history);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("test", result.RequestType);
        Assert.Equal("input text", result.InputText);
    }

    [Fact]
    public async Task GetRecentHistoryAsync_WithLimit_ShouldReturnLimitedResults()
    {
        // Arrange
        for (int i = 0; i < 15; i++)
        {
            await _service.SaveRequestAsync(new RequestHistory
            {
                RequestType = $"type{i}",
                InputText = $"input{i}",
                DetectedTask = $"task{i}",
                Response = $"response{i}"
            });
        }

        // Act
        var result = await _service.GetRecentHistoryAsync(5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task GetRecentHistoryAsync_OrderedByTimestamp_ShouldReturnNewestFirst()
    {
        // Arrange
        var first = await _service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "first",
            Timestamp = DateTime.UtcNow.AddMinutes(-2)
        });
        
        await Task.Delay(10); // Small delay
        
        var second = await _service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "second",
            Timestamp = DateTime.UtcNow
        });

        // Act
        var result = await _service.GetRecentHistoryAsync(10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("second", result[0].RequestType); // Newest first
        Assert.Equal("first", result[1].RequestType);
    }

    [Fact]
    public async Task GetHistoryByTypeAsync_ShouldReturnOnlyMatchingType()
    {
        // Arrange
        await _service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            DetectedTask = "reminder",
            InputText = "Remind me to call"
        });
        
        await _service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "other",
            DetectedTask = "time",
            InputText = "What time is it"
        });

        // Act
        var result = await _service.GetHistoryByTypeAsync("reminder");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("reminder", result[0].RequestType);
    }

    [Fact]
    public async Task DeleteReminderAsync_ValidId_ShouldDeleteSuccessfully()
    {
        // Arrange
        var reminder = await _service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            DetectedTask = "reminder",
            InputText = "Test reminder"
        });

        // Act
        var deleted = await _service.DeleteReminderAsync(reminder.Id);
        
        // Verify it's deleted
        var history = await _service.GetHistoryByIdAsync(reminder.Id);

        // Assert
        Assert.True(deleted);
        Assert.Null(history);
    }

    [Fact]
    public async Task DeleteReminderAsync_InvalidId_ShouldReturnFalse()
    {
        // Act
        var deleted = await _service.DeleteReminderAsync(Guid.NewGuid());

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public async Task UpdateReminderAsync_ValidId_ShouldUpdateSuccessfully()
    {
        // Arrange
        var reminder = await _service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            DetectedTask = "reminder",
            InputText = "Original reminder",
            Response = "Original response"
        });

        // Act
        var updated = await _service.UpdateReminderAsync(reminder.Id, "Updated reminder text");
        
        // Verify update
        var retrieved = await _service.GetHistoryByIdAsync(reminder.Id);

        // Assert
        Assert.NotNull(updated);
        Assert.NotNull(retrieved);
        Assert.Contains("Updated reminder text", retrieved!.InputText);
    }

    [Fact]
    public async Task UpdateReminderAsync_InvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _service.UpdateReminderAsync(Guid.NewGuid(), "New text");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecentHistoryAsync_EmptyDatabase_ShouldReturnEmptyList()
    {
        // Act
        var result = await _service.GetRecentHistoryAsync(10);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveRequestAsync_NullMediaStorageKey_ShouldSaveSuccessfully()
    {
        // Arrange
        var history = new RequestHistory
        {
            RequestType = "test",
            InputText = "input",
            DetectedTask = "task",
            Response = "response",
            MediaStorageKey = null
        };

        // Act
        var result = await _service.SaveRequestAsync(history);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.MediaStorageKey);
    }
}
