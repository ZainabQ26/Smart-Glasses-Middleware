using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class TaskDispatcherMoreTests
{
    private IConfiguration _config = null!;
    private ServiceProvider _serviceProvider = null!;
    private Mock<ILogger<TaskDispatcher>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<TaskDispatcher>>();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
    }

    private TaskDispatcher CreateDispatcher(IConfiguration? config = null)
        => new TaskDispatcher(config ?? _config, _serviceProvider, _loggerMock.Object);

    [Test]
    public void TryMatchByKeyword_WithNullInput_ReturnsNull()
    {
        var dispatcher = CreateDispatcher();
        var result = dispatcher.TryMatchByKeyword(null!);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryMatchByKeyword_WithWhitespaceInput_ReturnsNull()
    {
        var dispatcher = CreateDispatcher();
        var result = dispatcher.TryMatchByKeyword("   ");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetHandlerTypeMap_ContainsTimeHandler()
    {
        var dispatcher = CreateDispatcher();
        var map = dispatcher.GetHandlerTypeMap();
        Assert.That(map.ContainsKey("time"), Is.True);
    }

    [Test]
    public void GetHandlerTypeMap_ContainsReminderHandler()
    {
        var dispatcher = CreateDispatcher();
        var map = dispatcher.GetHandlerTypeMap();
        Assert.That(map.ContainsKey("reminder"), Is.True);
    }

    [Test]
    public async Task TryHandleAsync_UnknownTask_ReturnsFalse()
    {
        var dispatcher = CreateDispatcher();
        var (handled, response) = await dispatcher.TryHandleAsync("totally-unknown-task-xyz", "input");
        Assert.That(handled, Is.False);
        Assert.That(response, Is.Null);
    }

    [Test]
    public void KnownTasks_IsNotEmpty()
    {
        var dispatcher = CreateDispatcher();
        var tasks = dispatcher.KnownTasks;
        Assert.That(tasks, Is.Not.Null);
        Assert.That(tasks.Count, Is.GreaterThan(0));
    }

    [Test]
    public void KnownTasks_WithConfiguredTasks_ReturnsOnlyMatching()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:KnownTasks:0"] = "time",
                ["AI:KnownTasks:1"] = "reminder",
            })
            .Build();

        var dispatcher = CreateDispatcher(config);
        var tasks = dispatcher.KnownTasks;

        Assert.That(tasks, Contains.Item("time"));
        Assert.That(tasks, Contains.Item("reminder"));
    }

    [Test]
    public void TryMatchByKeyword_WithTimeKeyword_ReturnsTimeTask()
    {
        var dispatcher = CreateDispatcher();
        var result = dispatcher.TryMatchByKeyword("what time is it");
        Assert.That(result, Is.EqualTo("time"));
    }
}
