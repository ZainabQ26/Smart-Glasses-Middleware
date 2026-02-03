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

    [Fact]
    public async Task GetHistoryByTypeAsync_Returns_Only_Matching_Type()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Add reminders
        await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Buy milk",
            Response = "Reminder saved"
        });

        await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Call mom",
            Response = "Reminder saved"
        });

        // Add non-reminder
        await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "query",
            InputText = "What time is it?",
            Response = "It's 3:00 PM"
        });

        // Act
        var reminders = await service.GetHistoryByTypeAsync("reminder");

        // Assert
        Assert.Equal(2, reminders.Count);
        Assert.All(reminders, r => Assert.Equal("reminder", r.RequestType));
    }

    [Fact]
    public async Task GetHistoryByTypeAsync_Returns_Empty_List_When_No_Matches()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Add non-reminder items
        await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "query",
            InputText = "test",
            Response = "response"
        });

        // Act
        var reminders = await service.GetHistoryByTypeAsync("reminder");

        // Assert
        Assert.NotNull(reminders);
        Assert.Empty(reminders);
    }

    [Fact]
    public async Task GetHistoryByTypeAsync_Returns_Empty_List_For_Null_Type()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Act
        var result = await service.GetHistoryByTypeAsync(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetHistoryByTypeAsync_Returns_Empty_List_For_Whitespace_Type()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Act
        var result = await service.GetHistoryByTypeAsync("   ");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetHistoryByTypeAsync_Respects_Count_Limit()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Add 10 reminders
        for (int i = 0; i < 10; i++)
        {
            await service.SaveRequestAsync(new RequestHistory
            {
                RequestType = "reminder",
                InputText = $"Reminder {i}",
                Response = "Reminder saved"
            });
        }

        // Act
        var reminders = await service.GetHistoryByTypeAsync("reminder", 5);

        // Assert
        Assert.Equal(5, reminders.Count);
    }

    [Fact]
    public async Task GetHistoryByTypeAsync_Returns_In_Descending_Order()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var reminder1 = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "First reminder",
            Response = "Saved"
        });

        await Task.Delay(10);

        var reminder2 = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Second reminder",
            Response = "Saved"
        });

        // Act
        var reminders = await service.GetHistoryByTypeAsync("reminder");

        // Assert
        Assert.Equal(2, reminders.Count);
        Assert.Equal(reminder2.Id, reminders[0].Id); // Most recent first
        Assert.Equal(reminder1.Id, reminders[1].Id);
    }

    [Fact]
    public async Task UpdateReminderAsync_Updates_Reminder_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var reminder = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Buy milk",
            Response = "Reminder saved"
        });

        // Act
        var updated = await service.UpdateReminderAsync(reminder.Id, "Buy eggs");

        // Assert
        Assert.NotNull(updated);
        Assert.Equal("Buy eggs", updated.InputText);
        Assert.Equal(reminder.Id, updated.Id);
        Assert.Contains("updated", updated.Response!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateReminderAsync_Returns_Null_For_NonExistent_Reminder()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Act
        var updated = await service.UpdateReminderAsync(Guid.NewGuid(), "New text");

        // Assert
        Assert.Null(updated);
    }

    [Fact]
    public async Task UpdateReminderAsync_Throws_For_Empty_Text()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var reminder = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Buy milk",
            Response = "Reminder saved"
        });

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            service.UpdateReminderAsync(reminder.Id, ""));
    }

    [Fact]
    public async Task UpdateReminderAsync_Throws_For_Text_Too_Long()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var reminder = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Buy milk",
            Response = "Reminder saved"
        });

        var longText = new string('a', 1001);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            service.UpdateReminderAsync(reminder.Id, longText));
    }

    [Fact]
    public async Task UpdateReminderAsync_Throws_For_NonReminder_Type()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var history = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "query",
            InputText = "What time is it?",
            Response = "3:00 PM"
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.UpdateReminderAsync(history.Id, "New text"));
    }

    [Fact]
    public async Task UpdateReminderAsync_Trims_Input_Text()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var reminder = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Buy milk",
            Response = "Reminder saved"
        });

        // Act
        var updated = await service.UpdateReminderAsync(reminder.Id, "  Buy eggs  ");

        // Assert
        Assert.NotNull(updated);
        Assert.Equal("Buy eggs", updated.InputText); // Should be trimmed
    }

    [Fact]
    public async Task DeleteReminderAsync_Deletes_Reminder_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var reminder = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Buy milk",
            Response = "Reminder saved"
        });

        // Act
        var deleted = await service.DeleteReminderAsync(reminder.Id);

        // Assert
        Assert.True(deleted);
        var retrieved = await service.GetHistoryByIdAsync(reminder.Id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteReminderAsync_Returns_False_For_NonExistent_Reminder()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Act
        var deleted = await service.DeleteReminderAsync(Guid.NewGuid());

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteReminderAsync_Throws_For_NonReminder_Type()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var history = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "query",
            InputText = "What time is it?",
            Response = "3:00 PM"
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.DeleteReminderAsync(history.Id));
    }

    [Fact]
    public async Task DeleteReminderAsync_Is_Idempotent()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var reminder = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "reminder",
            InputText = "Buy milk",
            Response = "Reminder saved"
        });

        // Act
        var deleted1 = await service.DeleteReminderAsync(reminder.Id);
        var deleted2 = await service.DeleteReminderAsync(reminder.Id);

        // Assert
        Assert.True(deleted1);
        Assert.False(deleted2); // Second delete returns false (not found)
    }
}
