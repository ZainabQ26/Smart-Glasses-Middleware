using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
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
        Mock<ITranscriptionService>? transcriptionService = null,
        Mock<IHistoryService>? historyService = null,
        Mock<IRequestMonitoringService>? monitoringService = null)
    {
        var mockTranscription = transcriptionService ?? new Mock<ITranscriptionService>();
        var mockHistory = historyService ?? new Mock<IHistoryService>();
        var mockMonitoring = monitoringService ?? new Mock<IRequestMonitoringService>();
        var mockLogger = new Mock<ILogger<AudioController>>();

        var controller = new AudioController(
            mockTranscription.Object,
            mockHistory.Object,
            mockLogger.Object,
            mockMonitoring.Object);

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
        var controller = CreateController();

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
        var controller = CreateController();

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
        var controller = CreateController();

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
        var controller = CreateController();

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
        var controller = CreateController();

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
    public async Task ProcessAudio_TranscribesAudioWithoutStoringFile()
    {
        // Arrange
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockMonitoring = new Mock<IRequestMonitoringService>();

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test transcription");

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        mockMonitoring.Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("test-request-id");

        var controller = CreateController(mockTranscription, mockHistory, mockMonitoring);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        
        // Verify transcription was called
        mockTranscription.Verify(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            "audio/wav",
            It.IsAny<CancellationToken>()), Times.Once);
        
        // Verify history saved only transcription (no storage key)
        mockHistory.Verify(h => h.SaveRequestAsync(
            It.Is<RequestHistory>(r => 
                r.RequestType == "AudioTranscription" && 
                r.InputText == "Test transcription" &&
                r.MediaStorageKey == null)), // Key verification: no audio stored
            Times.Once);
    }

    [Fact]
    public async Task ProcessAudio_ReportsProgressDuringTranscription()
    {
        // Arrange
        var mockTranscription = new Mock<ITranscriptionService>();
        var mockHistory = new Mock<IHistoryService>();
        var mockMonitoring = new Mock<IRequestMonitoringService>();

        var requestId = "test-request-123";
        mockMonitoring.Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(requestId);

        mockTranscription.Setup(t => t.TranscribeAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test transcription");

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync((RequestHistory r) => { r.Id = Guid.NewGuid(); return r; });

        var controller = CreateController(mockTranscription, mockHistory, mockMonitoring);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio data");
        var request = new AudioRequest
        {
            AudioBase64 = Convert.ToBase64String(audioBytes)
        };

        // Act
        var result = await controller.ProcessAudio(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        
        // Verify progress was reported
        mockMonitoring.Verify(m => m.UpdateRequestStatus(
            requestId,
            "Processing",
            It.IsAny<string>(),
            It.IsAny<int>()), Times.AtLeastOnce);
        
        // Verify completion was reported
        mockMonitoring.Verify(m => m.CompleteRequest(
            requestId,
            true,
            It.IsAny<string>(),
            null), Times.Once);
    }
}
