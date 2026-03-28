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
public class MonitoringControllerTests
{
    private Mock<IRequestMonitoringService> _monitoringMock;
    private Mock<ILogger<MonitoringController>> _loggerMock;
    private MonitoringController _controller;

    [SetUp]
    public void Setup()
    {
        _monitoringMock = new Mock<IRequestMonitoringService>();
        _loggerMock = new Mock<ILogger<MonitoringController>>();
        _controller = new MonitoringController(_monitoringMock.Object, _loggerMock.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Test]
    public async Task GetRecentRequests_ReturnsOk()
    {
        _monitoringMock.Setup(x => x.GetRecentRequests(200)).ReturnsAsync(new List<RequestStatus>());

        var result = await _controller.GetRecentRequests() as OkObjectResult;
        Assert.That(result, Is.Not.Null);
    }
}
