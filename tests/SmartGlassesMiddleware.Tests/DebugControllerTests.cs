using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class DebugControllerTests
{
    private Mock<ITaskDispatcher> _taskDispatcherMock;
    private Mock<IConfiguration> _configMock;
    private DebugController _controller;

    [SetUp]
    public void Setup()
    {
        _taskDispatcherMock = new Mock<ITaskDispatcher>();
        _configMock = new Mock<IConfiguration>();
        _controller = new DebugController(_taskDispatcherMock.Object, _configMock.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Test]
    public void TaskStatus_ReturnsOk()
    {
        _taskDispatcherMock.Setup(x => x.KnownTasks).Returns(new List<string> { "task1" });
        _taskDispatcherMock.Setup(x => x.GetHandlerTypeMap()).Returns(new Dictionary<string, string>());

        var mockSection = new Mock<IConfigurationSection>();
        mockSection.Setup(s => s.Value).Returns("true");
        _configMock.Setup(c => c.GetSection(It.IsAny<string>())).Returns(mockSection.Object);

        var result = _controller.TaskStatus() as OkObjectResult;
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task EchoForm_NotForm_ReturnsBadRequest()
    {
        _controller.HttpContext.Request.ContentType = "application/json";
        var result = await _controller.EchoForm();
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task EchoRaw_ReturnsOk()
    {
        var result = await _controller.EchoRaw() as OkObjectResult;
        Assert.That(result, Is.Not.Null);
    }
}
