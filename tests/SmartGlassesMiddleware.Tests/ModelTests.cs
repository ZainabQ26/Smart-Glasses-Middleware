using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Moq;
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

    #region TaskRequest Tests

    [Fact]
    public void TaskRequest_ValidText_ShouldPass()
    {
        // Arrange
        var request = new TaskRequest { Text = "Valid task text" };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void TaskRequest_EmptyText_ShouldFail()
    {
        // Arrange
        var request = new TaskRequest { Text = "" };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.False(isValid);
        Assert.NotEmpty(results);
    }

    #endregion

    #region AudioRequest Tests

    [Fact]
    public void AudioRequest_ValidData_ShouldPass()
    {
        // Arrange
        var request = new AudioRequest
        {
            AudioBase64 = "base64encodedaudiodata",
            Transcription = "Optional transcription"
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void AudioRequest_NullTranscription_ShouldPass()
    {
        // Arrange
        var request = new AudioRequest
        {
            AudioBase64 = "validaudio",
            Transcription = null
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.True(isValid);
    }

    #endregion

    #region MediaRequest Tests

    [Fact]
    public void MediaRequest_ValidData_ShouldPass()
    {
        // Arrange
        var request = new MediaRequest
        {
            Prompt = "Process these files",
            Transcription = "Audio transcription"
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void MediaRequest_WithFiles_ShouldStoreFiles()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        var request = new MediaRequest
        {
            Files = new List<IFormFile> { mockFile.Object }
        };

        // Assert
        Assert.NotNull(request.Files);
        Assert.Single(request.Files);
    }

    #endregion

    #region TaskResponse Tests

    [Fact]
    public void TaskResponse_DefaultValues_ShouldBeSet()
    {
        // Arrange & Act
        var response = new TaskResponse();

        // Assert
        Assert.Equal(string.Empty, response.Task);
        Assert.Equal(string.Empty, response.Response);
        Assert.False(response.UsedAdvancedModel);
        Assert.Equal(Guid.Empty, response.RequestId);
    }

    [Fact]
    public void TaskResponse_SetProperties_ShouldPersist()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var response = new TaskResponse
        {
            Task = "reminder",
            Response = "Reminder saved",
            UsedAdvancedModel = true,
            RequestId = taskId
        };

        // Assert
        Assert.Equal("reminder", response.Task);
        Assert.Equal("Reminder saved", response.Response);
        Assert.True(response.UsedAdvancedModel);
        Assert.Equal(taskId, response.RequestId);
    }

    #endregion
}
