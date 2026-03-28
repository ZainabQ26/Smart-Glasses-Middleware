using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class TaskDispatcherTests
{
    private Mock<IConfiguration> _configMock;
    private Mock<IServiceProvider> _serviceProviderMock;
    private Mock<ILogger<TaskDispatcher>> _loggerMock;
    private TaskDispatcher _dispatcher;

    [SetUp]
    public void Setup()
    {
        _configMock = new Mock<IConfiguration>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<TaskDispatcher>>();

        var mockSection = new Mock<IConfigurationSection>();
        mockSection.Setup(s => s.GetChildren()).Returns(new List<IConfigurationSection>());
        _configMock.Setup(c => c.GetSection("AI:KnownTasks")).Returns(mockSection.Object);

        _dispatcher = new TaskDispatcher(_configMock.Object, _serviceProviderMock.Object, _loggerMock.Object);
    }

    [Test]
    public void GetHandlerTypeMap_ReturnsMap()
    {
        var map = _dispatcher.GetHandlerTypeMap();
        Assert.That(map, Is.Not.Null);
    }

    [Test]
    public async Task TryHandleAsync_EmptyTaskName_ReturnsFalse()
    {
        var (handled, resp) = await _dispatcher.TryHandleAsync("", "input");
        Assert.That(handled, Is.False);
    }

    [Test]
    public void TryMatchByKeyword_EmptyInput_ReturnsNull()
    {
        var match = _dispatcher.TryMatchByKeyword("");
        Assert.That(match, Is.Null);
    }

    [Test]
    public void KnownTasks_ReturnsList()
    {
        var tasks = _dispatcher.KnownTasks;
        Assert.That(tasks, Is.Not.Null);
    }
}
