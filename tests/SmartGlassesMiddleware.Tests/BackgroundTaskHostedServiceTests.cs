using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

public class BackgroundTaskHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesTask_Successfully()
    {
        // Arrange
        var mockQueue = new Mock<IBackgroundTaskQueue>();
        var mockMediaProcessor = new Mock<IMediaProcessor>();
        var mockLogger = new Mock<ILogger<BackgroundTaskHostedService>>();

        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            PromptText = "test",
            Status = BackgroundTaskStatus.Queued
        };

        var cts = new CancellationTokenSource();

        mockQueue.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry)
            .ReturnsAsync((BackgroundTaskEntry?)null); // Return null to stop the loop

        mockQueue.Setup(q => q.UpdateAsync(It.IsAny<BackgroundTaskEntry>()))
            .Returns(Task.CompletedTask)
            .Callback<BackgroundTaskEntry>(e =>
            {
                if (e.Status == BackgroundTaskStatus.Completed)
                {
                    cts.Cancel(); // Cancel to stop the service after completion
                }
            });

        mockMediaProcessor.Setup(p => p.ProcessBackgroundTaskAsync(It.IsAny<BackgroundTaskEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Processing complete");

        var services = new ServiceCollection();
        services.AddScoped<IMediaProcessor>(sp => mockMediaProcessor.Object);
        var serviceProvider = services.BuildServiceProvider();

        var service = new BackgroundTaskHostedService(mockQueue.Object, serviceProvider, mockLogger.Object);

        // Act
        var executeTask = service.StartAsync(cts.Token);
        await Task.Delay(500); // Give it time to process
        cts.Cancel();
        await executeTask;

        // Assert
        mockQueue.Verify(q => q.DequeueAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockMediaProcessor.Verify(p => p.ProcessBackgroundTaskAsync(It.IsAny<BackgroundTaskEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesException_AndMarksFailed()
    {
        // Arrange
        var mockQueue = new Mock<IBackgroundTaskQueue>();
        var mockMediaProcessor = new Mock<IMediaProcessor>();
        var mockLogger = new Mock<ILogger<BackgroundTaskHostedService>>();

        var taskId = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = taskId,
            PromptText = "test",
            Status = BackgroundTaskStatus.Queued
        };

        var cts = new CancellationTokenSource();
        BackgroundTaskEntry? failedEntry = null;

        mockQueue.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry)
            .ReturnsAsync((BackgroundTaskEntry?)null);

        mockQueue.Setup(q => q.UpdateAsync(It.IsAny<BackgroundTaskEntry>()))
            .Returns(Task.CompletedTask)
            .Callback<BackgroundTaskEntry>(e =>
            {
                if (e.Status == BackgroundTaskStatus.Failed)
                {
                    failedEntry = e;
                    cts.Cancel();
                }
            });

        mockMediaProcessor.Setup(p => p.ProcessBackgroundTaskAsync(It.IsAny<BackgroundTaskEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Processing error"));

        var services = new ServiceCollection();
        services.AddScoped<IMediaProcessor>(sp => mockMediaProcessor.Object);
        var serviceProvider = services.BuildServiceProvider();

        var service = new BackgroundTaskHostedService(mockQueue.Object, serviceProvider, mockLogger.Object);

        // Act
        var executeTask = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await executeTask;

        // Assert
        Assert.NotNull(failedEntry);
        Assert.Equal(BackgroundTaskStatus.Failed, failedEntry!.Status);
        Assert.Equal("Processing error", failedEntry.Error);
    }
}
