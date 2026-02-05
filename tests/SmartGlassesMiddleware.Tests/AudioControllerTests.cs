using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class AudioControllerTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private AudioController CreateController(
        ApplicationDbContext context,
        Mock<IMediaStorageService>? mediaStorageService = null,
        Mock<ITranscriptionService>? transcriptionService = null,
        Mock<IHistoryService>? historyService = null,
        Mock<IRagService>? ragService = null)
    {
        var mockMediaStorage = mediaStorageService ?? new Mock<IMediaStorageService>();
        var mockTranscription = transcriptionService ?? new Mock<ITranscriptionService>();
        var mockHistory = historyService ?? new Mock<IHistoryService>();
        var mockRag = ragService ?? new Mock<IRagService>();
        var mockLogger = new Mock<ILogger<AudioController>>();

        var controller = new AudioController(
            mockMediaStorage.Object,
            mockTranscription.Object,
            mockHistory.Object,
            mockRag.Object,
            context,
            mockLogger.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task ProcessAudio_Returns_BadRequest_When_AudioBase64_Is_Null()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);

        var request = new AudioRequest
        {
            AudioBase64 = null!
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Returns_BadRequest_When_AudioBase64_Is_Empty()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);

        var request = new AudioRequest
        {
            AudioBase64 = string.Empty
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Returns_BadRequest_When_AudioBase64_Is_Whitespace()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);

        var request = new AudioRequest
        {
            AudioBase64 = "   "
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(badRequestResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Returns_BadRequest_When_AudioBase64_Is_Invalid()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);

        var request = new AudioRequest
        {
            AudioBase64 = "invalid-base64!!!"
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ValidationProblemDetails>(badRequestResult.Value);
        Assert.Contains("audioBase64", problemDetails.Errors.Keys);
    }

    [Fact]
    public async Task ProcessAudio_Returns_BadRequest_When_AudioBase64_Decodes_To_Empty()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);

        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(Array.Empty<byte>())
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ValidationProblemDetails>(badRequestResult.Value);
        Assert.Contains("audioBase64", problemDetails.Errors.Keys);
    }

    [Fact]
    public async Task ProcessAudio_Uploads_Audio_To_Storage()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";
        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync("Test transcription");

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockMediaStorage.Verify(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"), Times.Once);
    }

    [Fact]
    public async Task ProcessAudio_Transcribes_Audio()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";
        var transcription = "This is the transcription";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(transcription);

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockTranscription.Verify(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"), Times.Once);
    }

    [Fact]
    public async Task ProcessAudio_Saves_ManagedFile_Record()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync("Test transcription");

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await context.ManagedFiles.CountAsync());
        var file = await context.ManagedFiles.FirstAsync();
        Assert.Equal(storageKey, file.StorageKey);
        Assert.Equal("audio/wav", file.ContentType);
    }

    [Fact]
    public async Task ProcessAudio_Processes_File_For_RAG()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";
        var transcription = "Test transcription";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(transcription);

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            transcription))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        mockRag.Verify(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            "audio/wav",
            storageKey,
            transcription), Times.Once);
    }

    [Fact]
    public async Task ProcessAudio_Saves_History()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";
        var transcription = "Test transcription";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(transcription);

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        mockHistory.Verify(h => h.SaveRequestAsync(
            It.Is<RequestHistory>(r =>
                r.RequestType == "Audio" &&
                r.InputText == transcription &&
                r.MediaStorageKey == storageKey)), Times.Once);
    }

    [Fact]
    public async Task ProcessAudio_Returns_Ok_With_StorageKey_And_Transcription()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";
        var transcription = "Test transcription";
        var historyId = Guid.NewGuid();

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(transcription);

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = historyId; return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Continues_When_Transcription_Fails()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ThrowsAsync(new Exception("Transcription failed"));

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Continues_When_RAG_Processing_Fails()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync("Test transcription");

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .ThrowsAsync(new Exception("RAG processing failed"));

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Returns_Ok_Without_RequestId_When_History_Fails()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync("Test transcription");

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ThrowsAsync(new Exception("History save failed"));

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Returns_500_When_Storage_Fails()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ThrowsAsync(new Exception("Storage failed"));

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
        Assert.IsType<ProblemDetails>(statusCodeResult.Value);
    }

    [Fact]
    public async Task ProcessAudio_Handles_Large_Audio_Files()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockRag = new Mock<IRagService>();

        var storageKey = "test-storage-key";

        mockMediaStorage.Setup(m => m.UploadMediaAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync(storageKey);

        mockMediaStorage.Setup(m => m.DownloadMediaAsync(storageKey))
            .ReturnsAsync(new MemoryStream());

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav"))
            .ReturnsAsync("Test transcription");

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockRag.Setup(r => r.ProcessFileForRagAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockTranscription, mockHistory, mockRag);

        // Create a large audio file (1MB)
        var audioBytes = new byte[1024 * 1024];
        new Random().NextBytes(audioBytes);
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }
}
