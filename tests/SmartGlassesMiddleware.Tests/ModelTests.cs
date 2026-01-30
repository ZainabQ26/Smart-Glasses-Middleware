using System;
using System.Collections.Generic;
using Xunit;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests;

public class ModelTests
{
    [Fact]
    public void BackgroundTaskEntry_Initializes_With_New_Guid()
    {
        // Act
        var entry = new BackgroundTaskEntry();

        // Assert
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    [Fact]
    public void BackgroundTaskEntry_Defaults_To_Queued_Status()
    {
        // Act
        var entry = new BackgroundTaskEntry();

        // Assert
        Assert.Equal(BackgroundTaskStatus.Queued, entry.Status);
    }

    [Fact]
    public void BackgroundTaskEntry_Sets_CreatedAt_To_Current_Time()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var entry = new BackgroundTaskEntry();
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(entry.CreatedAt >= before && entry.CreatedAt <= after);
    }

    [Fact]
    public void BackgroundTaskEntry_MediaItems_Initializes_As_Empty_List()
    {
        // Act
        var entry = new BackgroundTaskEntry();

        // Assert
        Assert.NotNull(entry.MediaItems);
        Assert.Empty(entry.MediaItems);
    }

    [Fact]
    public void BackgroundMediaItem_Can_Be_Created()
    {
        // Act
        var item = new BackgroundMediaItem
        {
            FileName = "test.jpg",
            ContentType = "image/jpeg",
            StorageKey = "storage-key-123",
            Transcription = "test transcription"
        };

        // Assert
        Assert.Equal("test.jpg", item.FileName);
        Assert.Equal("image/jpeg", item.ContentType);
        Assert.Equal("storage-key-123", item.StorageKey);
        Assert.Equal("test transcription", item.Transcription);
    }

    [Fact]
    public void BackgroundTaskStatus_Has_All_States()
    {
        // Assert
        Assert.True(Enum.IsDefined(typeof(BackgroundTaskStatus), BackgroundTaskStatus.Queued));
        Assert.True(Enum.IsDefined(typeof(BackgroundTaskStatus), BackgroundTaskStatus.Processing));
        Assert.True(Enum.IsDefined(typeof(BackgroundTaskStatus), BackgroundTaskStatus.Completed));
        Assert.True(Enum.IsDefined(typeof(BackgroundTaskStatus), BackgroundTaskStatus.Failed));
    }

    [Fact]
    public void RequestHistory_Can_Be_Created()
    {
        // Act
        var history = new RequestHistory
        {
            RequestType = "test",
            InputText = "input",
            DetectedTask = "task",
            Response = "response",
            UsedAdvancedModel = true,
            MediaStorageKey = "key"
        };

        // Assert
        Assert.Equal("test", history.RequestType);
        Assert.Equal("input", history.InputText);
        Assert.Equal("task", history.DetectedTask);
        Assert.Equal("response", history.Response);
        Assert.True(history.UsedAdvancedModel);
        Assert.Equal("key", history.MediaStorageKey);
    }
}
