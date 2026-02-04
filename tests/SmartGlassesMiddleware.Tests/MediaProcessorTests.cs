using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class MediaProcessorTests
{
    [Fact]
    public async Task ProcessBackgroundTaskAsync_Transcribes_Audio_Files()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        var audioStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        mockMediaStorage.Setup(m => m.DownloadMediaAsync(It.IsAny<string>()))
            .ReturnsAsync(audioStream);

        mockTranscription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Hello world");

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("audio_task", "AI response", false));

        var savedHistory = new RequestHistory { Id = Guid.NewGuid() };
        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(savedHistory);

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var entry = new BackgroundTaskEntry
        {
            Id = Guid.NewGuid(),
            PromptText = "Process this audio",
            MediaItems = new List<BackgroundMediaItem>
            {
                new BackgroundMediaItem
                {
                    FileName = "audio.wav",
                    ContentType = "audio/wav",
                    StorageKey = "key123"
                }
            }
        };

        // Act
        var result = await processor.ProcessBackgroundTaskAsync(entry);

        // Assert
        mockTranscription.Verify(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            "audio.wav",
            "audio/wav",
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("Hello world", entry.MediaItems[0].Transcription);
    }

    [Fact]
    public async Task ProcessBackgroundTaskAsync_Skips_Already_Transcribed_Files()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("task", "response", false));

        var savedHistory = new RequestHistory { Id = Guid.NewGuid() };
        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(savedHistory);

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var entry = new BackgroundTaskEntry
        {
            MediaItems = new List<BackgroundMediaItem>
            {
                new BackgroundMediaItem
                {
                    FileName = "audio.wav",
                    ContentType = "audio/wav",
                    StorageKey = "key123",
                    Transcription = "Already transcribed"
                }
            }
        };

        // Act
        await processor.ProcessBackgroundTaskAsync(entry);

        // Assert
        mockTranscription.Verify(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBackgroundTaskAsync_Handles_Transcription_Failure()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        var audioStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        mockMediaStorage.Setup(m => m.DownloadMediaAsync(It.IsAny<string>()))
            .ReturnsAsync(audioStream);

        mockTranscription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Transcription error"));

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("task", "response", false));

        var savedHistory = new RequestHistory { Id = Guid.NewGuid() };
        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(savedHistory);

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var entry = new BackgroundTaskEntry
        {
            MediaItems = new List<BackgroundMediaItem>
            {
                new BackgroundMediaItem
                {
                    FileName = "audio.wav",
                    ContentType = "audio/wav",
                    StorageKey = "key123"
                }
            }
        };

        // Act
        var result = await processor.ProcessBackgroundTaskAsync(entry);

        // Assert - should continue processing despite transcription failure
        Assert.NotNull(result);
        mockAI.Verify(a => a.ProcessRequestAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBackgroundTaskAsync_Processes_With_AI()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("media_task", "AI processed response", true));

        var savedHistory = new RequestHistory { Id = Guid.NewGuid() };
        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(savedHistory);

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var entry = new BackgroundTaskEntry
        {
            PromptText = "Analyze this",
            MediaItems = new List<BackgroundMediaItem>
            {
                new BackgroundMediaItem
                {
                    FileName = "image.jpg",
                    ContentType = "image/jpeg",
                    StorageKey = "key456",
                    Transcription = null
                }
            }
        };

        // Act
        var result = await processor.ProcessBackgroundTaskAsync(entry);

        // Assert
        mockAI.Verify(a => a.ProcessRequestAsync(It.Is<string>(s =>
            s.Contains("Analyze this") &&
            s.Contains("image.jpg"))), Times.Once);
    }

    [Fact]
    public async Task ProcessBackgroundTaskAsync_Saves_History()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("task", "response", false));

        var savedHistory = new RequestHistory { Id = Guid.NewGuid() };
        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(savedHistory);

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var entry = new BackgroundTaskEntry
        {
            MediaItems = new List<BackgroundMediaItem>
            {
                new BackgroundMediaItem
                {
                    FileName = "test.mp3",
                    ContentType = "audio/mp3",
                    StorageKey = "key789"
                }
            }
        };

        // Act
        await processor.ProcessBackgroundTaskAsync(entry);

        // Assert
        mockHistory.Verify(h => h.SaveRequestAsync(It.Is<RequestHistory>(r =>
            r.RequestType == "Media" &&
            r.DetectedTask == "task" &&
            r.Response == "response")), Times.Once);
    }

    [Fact]
    public async Task ProcessBackgroundTaskAsync_Returns_Result_With_RequestId()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("task", "response", false));

        var historyId = Guid.NewGuid();
        var savedHistory = new RequestHistory { Id = historyId };
        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(savedHistory);

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var entry = new BackgroundTaskEntry
        {
            MediaItems = new List<BackgroundMediaItem>
            {
                new BackgroundMediaItem
                {
                    FileName = "test.mp3",
                    ContentType = "audio/mp3",
                    StorageKey = "key"
                }
            }
        };

        // Act
        var result = await processor.ProcessBackgroundTaskAsync(entry);

        // Assert
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains(historyId.ToString(), json);
    }

    [Fact]
    public async Task ProcessBackgroundTaskAsync_Handles_History_Save_Failure()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("task", "response", false));

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ThrowsAsync(new Exception("Database error"));

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var entry = new BackgroundTaskEntry
        {
            MediaItems = new List<BackgroundMediaItem>
            {
                new BackgroundMediaItem
                {
                    FileName = "test.mp3",
                    ContentType = "audio/mp3",
                    StorageKey = "key"
                }
            }
        };

        // Act
        var result = await processor.ProcessBackgroundTaskAsync(entry);

        // Assert - should still return result
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("response", json);
    }

    [Fact]
    public async Task ProcessBackgroundTaskAsync_Identifies_Audio_By_Extension()
    {
        // Arrange
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockAI = new Mock<IAIService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<MediaProcessor>>();

        var audioStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        mockMediaStorage.Setup(m => m.DownloadMediaAsync(It.IsAny<string>()))
            .ReturnsAsync(audioStream);

        mockTranscription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Transcribed");

        mockAI.Setup(a => a.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("task", "response", false));

        var savedHistory = new RequestHistory { Id = Guid.NewGuid() };
        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(savedHistory);

        var processor = new MediaProcessor(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockAI.Object,
            mockHistory.Object,
            mockLogger.Object);

        var testExtensions = new[] { ".wav", ".mp3", ".m4a", ".flac", ".aac" };

        foreach (var ext in testExtensions)
        {
            var entry = new BackgroundTaskEntry
            {
                MediaItems = new List<BackgroundMediaItem>
                {
                    new BackgroundMediaItem
                    {
                        FileName = $"audio{ext}",
                        ContentType = "application/octet-stream",
                        StorageKey = "key"
                    }
                }
            };

            // Act
            await processor.ProcessBackgroundTaskAsync(entry);
        }

        // Assert - should be called once per extension
        mockTranscription.Verify(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(testExtensions.Length));
    }
}
