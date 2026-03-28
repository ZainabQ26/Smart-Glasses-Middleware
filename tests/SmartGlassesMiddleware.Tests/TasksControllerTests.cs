using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class TasksControllerTests
{
    private Mock<IBackgroundTaskQueue> _queueMock;
    private TasksController _controller;

    [SetUp]
    public void Setup()
    {
        _queueMock = new Mock<IBackgroundTaskQueue>();
        _controller = new TasksController(_queueMock.Object);
    }

    [Test]
    public void GetTask_NotFound_ReturnsNotFound()
    {
        _queueMock.Setup(x => x.Get(It.IsAny<Guid>())).Returns((BackgroundTaskEntry?)null);
        var result = _controller.GetTask(Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void GetTask_Completed_ReturnsOk()
    {
        var entry = new BackgroundTaskEntry { Id = Guid.NewGuid(), Status = BackgroundTaskStatus.Completed };
        _queueMock.Setup(x => x.Get(It.IsAny<Guid>())).Returns(entry);

        var result = _controller.GetTask(Guid.NewGuid()) as OkObjectResult;
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void GetTask_Failed_Returns500()
    {
        var entry = new BackgroundTaskEntry { Id = Guid.NewGuid(), Status = BackgroundTaskStatus.Failed };
        _queueMock.Setup(x => x.Get(It.IsAny<Guid>())).Returns(entry);

        var result = _controller.GetTask(Guid.NewGuid()) as ObjectResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public void GetTask_Processing_ReturnsAccepted()
    {
        var entry = new BackgroundTaskEntry { Id = Guid.NewGuid(), Status = BackgroundTaskStatus.Processing };
        _queueMock.Setup(x => x.Get(It.IsAny<Guid>())).Returns(entry);

        var result = _controller.GetTask(Guid.NewGuid()) as AcceptedResult;
        Assert.That(result, Is.Not.Null);
    }
}
