using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class HistoryServiceTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SaveRequestAsync_Saves_History_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var history = new RequestHistory
        {
            RequestType = "test",
            InputText = "test input",
            Response = "test response"
        };

        // Act
        var savedHistory = await service.SaveRequestAsync(history);

        // Assert
        Assert.NotEqual(Guid.Empty, savedHistory.Id);
        Assert.True(savedHistory.Timestamp > DateTime.MinValue);
        Assert.Equal(1, await context.RequestHistories.CountAsync());
    }

    [Fact]
    public async Task GetRecentHistoryAsync_Returns_Correct_Count()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Add 5 history items
        for (int i = 0; i < 5; i++)
        {
            await service.SaveRequestAsync(new RequestHistory
            {
                RequestType = $"test{i}",
                InputText = $"input{i}",
                Response = $"response{i}"
            });
        }

        // Act
        var recentHistory = await service.GetRecentHistoryAsync(3);

        // Assert
        Assert.Equal(3, recentHistory.Count);
    }

    [Fact]
    public async Task GetRecentHistoryAsync_Returns_Items_In_Descending_Order()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var history1 = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "first",
            InputText = "input1",
            Response = "response1"
        });

        await Task.Delay(10); // Small delay to ensure different timestamps

        var history2 = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "second",
            InputText = "input2",
            Response = "response2"
        });

        // Act
        var recentHistory = await service.GetRecentHistoryAsync(10);

        // Assert
        Assert.Equal(2, recentHistory.Count);
        Assert.Equal(history2.Id, recentHistory[0].Id); // Most recent first
        Assert.Equal(history1.Id, recentHistory[1].Id);
    }

    [Fact]
    public async Task GetHistoryByIdAsync_Returns_Correct_History()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var savedHistory = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "test",
            InputText = "input",
            Response = "response"
        });

        // Act
        var retrievedHistory = await service.GetHistoryByIdAsync(savedHistory.Id);

        // Assert
        Assert.NotNull(retrievedHistory);
        Assert.Equal(savedHistory.Id, retrievedHistory.Id);
        Assert.Equal("test", retrievedHistory.RequestType);
    }

    [Fact]
    public async Task GetHistoryByIdAsync_Returns_Null_When_Not_Found()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Act
        var retrievedHistory = await service.GetHistoryByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(retrievedHistory);
    }
}
