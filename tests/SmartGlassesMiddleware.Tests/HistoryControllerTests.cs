using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class HistoryControllerTests
{
    private Mock<IHistoryService> _historyServiceMock;
    private Mock<ILogger<HistoryController>> _loggerMock;
    private HistoryController _controller;

    [SetUp]
    public void Setup()
    {
        _historyServiceMock = new Mock<IHistoryService>();
        _loggerMock = new Mock<ILogger<HistoryController>>();

        _controller = new HistoryController(_historyServiceMock.Object, _loggerMock.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Test]
    public async Task GetReminders_ReturnsOk()
    {
        _historyServiceMock.Setup(x => x.GetHistoryByTypeAsync("reminder", 100)).ReturnsAsync(new List<RequestHistory>());
        var result = await _controller.GetReminders();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetHistoryByType_ReturnsOk()
    {
        _historyServiceMock.Setup(x => x.GetHistoryByTypeAsync("test", 100)).ReturnsAsync(new List<RequestHistory>());
        var result = await _controller.GetHistoryByType("test");
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task UpdateReminder_InvalidRequest_ReturnsBadRequest()
    {
        var result = await _controller.UpdateReminder(Guid.NewGuid(), new UpdateReminderRequest { Text = "" });
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UpdateReminder_NotFound_ReturnsNotFound()
    {
        _historyServiceMock.Setup(x => x.UpdateReminderAsync(It.IsAny<Guid>(), "test")).ReturnsAsync((RequestHistory?)null);
        var result = await _controller.UpdateReminder(Guid.NewGuid(), new UpdateReminderRequest { Text = "test" });
        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task DeleteReminder_NotFound_ReturnsNotFound()
    {
        _historyServiceMock.Setup(x => x.DeleteReminderAsync(It.IsAny<Guid>())).ReturnsAsync(false);
        var result = await _controller.DeleteReminder(Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task DeleteReminder_Success_ReturnsNoContent()
    {
        _historyServiceMock.Setup(x => x.DeleteReminderAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        var result = await _controller.DeleteReminder(Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public async Task QueryReminders_InvalidQuery_ReturnsBadRequest()
    {
        var result = await _controller.QueryReminders(new QueryRequest { Query = "" });
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task QueryReminders_ValidQuery_ReturnsOk()
    {
        var result = await _controller.QueryReminders(new QueryRequest { Query = "test" });
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }
}
