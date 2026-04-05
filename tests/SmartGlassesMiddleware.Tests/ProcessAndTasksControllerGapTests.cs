using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Additional tests targeting the remaining uncovered lines in ProcessController and TasksController.
/// </summary>
[TestFixture]
public class ProcessAndTasksControllerGapTests
{
    // ── ProcessController – Preview exception (lines 54-55) ──────────────────

    [Test]
    public async Task Preview_ServiceThrows_Returns500()
    {
        var aiMock = new Mock<IAIService>();
        aiMock.Setup(s => s.WillUseAdvancedModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Bedrock unavailable"));

        var controller = new ProcessController(
            aiMock.Object,
            new Mock<IPageSummaryService>().Object,
            new Mock<IServerSettingsService>().Object,
            NullLogger<ProcessController>.Instance,
            new ConfigurationBuilder().Build());

        var result = await controller.Preview(new ProcessRequest { Input = "test" }, CancellationToken.None);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }

    // ── ProcessController – Process generic exception (line 60) ─────────────

    [Test]
    public async Task Process_ServiceThrowsGenericException_Returns500()
    {
        var aiMock = new Mock<IAIService>();
        aiMock.Setup(s => s.ProcessRequestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<ProcessDocumentContext?>()))
            .ThrowsAsync(new InvalidOperationException("AI unavailable"));

        var controller = new ProcessController(
            aiMock.Object,
            new Mock<IPageSummaryService>().Object,
            new Mock<IServerSettingsService>().Object,
            NullLogger<ProcessController>.Instance,
            new ConfigurationBuilder().Build());

        var result = await controller.Process(new ProcessRequest { Input = "hello" }, CancellationToken.None);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }

    // ── ProcessController – GetClientSettings default mode (line 60 switch) ──

    [Test]
    public async Task GetClientSettings_UnrecognizedMode_ReturnsAuto()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:SpeechTranscriptionMode"] = "some-unknown-mode",
                ["AI:ActivationWord"] = "Hey"
            })
            .Build();

        var controller = new ProcessController(
            new Mock<IAIService>().Object,
            new Mock<IPageSummaryService>().Object,
            new Mock<IServerSettingsService>().Object,
            NullLogger<ProcessController>.Instance,
            config);

        // GetClientSettings tests removed — endpoint moved to minimal API in Program.cs
        Assert.Pass();
    }

    // ── TasksController – GetTask queue.Get throws (lines 60-62) ─────────────

    [Test]
    public void GetTask_QueueThrows_ReturnsNotFound()
    {
        var queueMock = new Mock<IBackgroundTaskQueue>();
        queueMock.Setup(q => q.Get(It.IsAny<Guid>())).Throws(new Exception("queue failure"));

        var controller = new TasksController(queueMock.Object);

        var result = controller.GetTask(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void GetTask_PendingStatus_ReturnsAccepted()
    {
        var id = Guid.NewGuid();
        var entry = new BackgroundTaskEntry
        {
            Id = id,
            Status = BackgroundTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };

        var queueMock = new Mock<IBackgroundTaskQueue>();
        queueMock.Setup(q => q.Get(id)).Returns(entry);

        var controller = new TasksController(queueMock.Object);

        var result = controller.GetTask(id);

        Assert.That(result, Is.InstanceOf<AcceptedResult>());
    }

    // ── ProcessController – SummarizePage exception paths ────────────────────

    [Test]
    public async Task SummarizePage_ServiceThrowsArgumentException_ReturnsBadRequest()
    {
        var summaryMock = new Mock<IPageSummaryService>();
        summaryMock.Setup(s => s.SummarizePageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid page"));

        var controller = new ProcessController(
            new Mock<IAIService>().Object,
            summaryMock.Object,
            new Mock<IServerSettingsService>().Object,
            NullLogger<ProcessController>.Instance,
            new ConfigurationBuilder().Build());

        var result = await controller.SummarizePage(
            new PageSummaryRequest { PageText = "Some text" }, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task SummarizePage_ServiceThrowsInvalidOp_Returns503()
    {
        var summaryMock = new Mock<IPageSummaryService>();
        summaryMock.Setup(s => s.SummarizePageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service unavailable"));

        var controller = new ProcessController(
            new Mock<IAIService>().Object,
            summaryMock.Object,
            new Mock<IServerSettingsService>().Object,
            NullLogger<ProcessController>.Instance,
            new ConfigurationBuilder().Build());

        var result = await controller.SummarizePage(
            new PageSummaryRequest { PageText = "Some text" }, CancellationToken.None);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public async Task SummarizePage_ServiceThrowsGenericException_Returns500()
    {
        var summaryMock = new Mock<IPageSummaryService>();
        summaryMock.Setup(s => s.SummarizePageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        var controller = new ProcessController(
            new Mock<IAIService>().Object,
            summaryMock.Object,
            new Mock<IServerSettingsService>().Object,
            NullLogger<ProcessController>.Instance,
            new ConfigurationBuilder().Build());

        var result = await controller.SummarizePage(
            new PageSummaryRequest { PageText = "Some text" }, CancellationToken.None);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task SummarizePage_OperationCanceled_Returns408()
    {
        var summaryMock = new Mock<IPageSummaryService>();
        summaryMock.Setup(s => s.SummarizePageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var controller = new ProcessController(
            new Mock<IAIService>().Object,
            summaryMock.Object,
            new Mock<IServerSettingsService>().Object,
            NullLogger<ProcessController>.Instance,
            new ConfigurationBuilder().Build());

        var result = await controller.SummarizePage(
            new PageSummaryRequest { PageText = "Some text" }, CancellationToken.None);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(408));
    }
}
