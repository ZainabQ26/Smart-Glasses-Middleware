using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Hubs;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class RequestMonitoringServiceTests
{
    private Mock<IHubContext<TaskHub>> _hubMock;
    private Mock<ILogger<RequestMonitoringService>> _loggerMock;
    private Mock<IConfiguration> _configMock;
    private Mock<IServiceScopeFactory> _scopeFactoryMock;
    private RequestMonitoringService _service;

    [SetUp]
    public void Setup()
    {
        _hubMock = new Mock<IHubContext<TaskHub>>();
        _loggerMock = new Mock<ILogger<RequestMonitoringService>>();
        _configMock = new Mock<IConfiguration>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        var mockSection = new Mock<IConfigurationSection>();
        mockSection.Setup(s => s.Value).Returns("100");
        _configMock.Setup(c => c.GetSection("Monitoring:MaxRecentRequests")).Returns(mockSection.Object);

        _service = new RequestMonitoringService(
            _hubMock.Object,
            _loggerMock.Object,
            _configMock.Object,
            _scopeFactoryMock.Object);
    }

    [Test]
    public async Task StartRequest_Works()
    {
        var id = await _service.StartRequest("testType", "testDesc");
        Assert.That(id, Is.Not.Null.And.Not.Empty);

        var recent = await _service.GetRecentRequests();
        Assert.That(recent.Count, Is.EqualTo(1));
        Assert.That(recent[0].RequestType, Is.EqualTo("testType"));
    }

    [Test]
    public async Task UpdateRequestStatus_Works()
    {
        var id = await _service.StartRequest("testType", "testDesc");
        await _service.UpdateRequestStatus(id, "Updating", "details", 50);

        var recent = await _service.GetRecentRequests();
        Assert.That(recent[0].Status, Is.EqualTo("Updating"));
        Assert.That(recent[0].Progress, Is.EqualTo(50));
    }
}
