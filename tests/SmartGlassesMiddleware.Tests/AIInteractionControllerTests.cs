using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class AIInteractionControllerTests
{
    private ApplicationDbContext _dbContext;
    private Mock<ILogger<AIInteractionController>> _loggerMock;
    private AIInteractionController _controller;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _loggerMock = new Mock<ILogger<AIInteractionController>>();
        _controller = new AIInteractionController(_dbContext, _loggerMock.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task GetInteractionLogs_ReturnsOk()
    {
        var log = new AIInteractionLog { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, ModelUsed = "test" };
        _dbContext.AIInteractionLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetInteractionLogs();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetInteractionLog_NotFound_ReturnsNotFound()
    {
        var result = await _controller.GetInteractionLog(Guid.NewGuid());
        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetInteractionLog_Found_ReturnsOk()
    {
        var log = new AIInteractionLog { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, ModelUsed = "test" };
        _dbContext.AIInteractionLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetInteractionLog(log.Id);
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetStats_ReturnsOk()
    {
        var log = new AIInteractionLog { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, ModelUsed = "test", Success = true };
        _dbContext.AIInteractionLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetStats();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }
}
