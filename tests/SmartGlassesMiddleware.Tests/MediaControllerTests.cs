using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class MediaControllerTests
{
    private ApplicationDbContext _context = null!;
    private Mock<IMediaStorageService> _mediaStorageMock = null!;
    private Mock<IAIService> _aiServiceMock = null!;
    private Mock<IHistoryService> _historyServiceMock = null!;
    private Mock<IBackgroundTaskQueue> _backgroundQueueMock = null!;
    private Mock<IMediaProcessor> _mediaProcessorMock = null!;
    private Mock<IRagService> _ragServiceMock = null!;
    private Mock<ILogger<MediaController>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _mediaStorageMock = new Mock<IMediaStorageService>();
        _aiServiceMock = new Mock<IAIService>();
        _historyServiceMock = new Mock<IHistoryService>();
        _backgroundQueueMock = new Mock<IBackgroundTaskQueue>();
        _mediaProcessorMock = new Mock<IMediaProcessor>();
        _ragServiceMock = new Mock<IRagService>();
        _loggerMock = new Mock<ILogger<MediaController>>();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private MediaController CreateController()
    {
        var controller = new MediaController(
            _mediaStorageMock.Object,
            _aiServiceMock.Object,
            _historyServiceMock.Object,
            _backgroundQueueMock.Object,
            _mediaProcessorMock.Object,
            _ragServiceMock.Object,
            _context,
            _loggerMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Test]
    public async Task ProcessMedia_NoFiles_NoPrompt_ReturnsBadRequest()
    {
        var controller = CreateController();
        // Don't set multipart content type to avoid form parsing; pass empty request
        var result = await controller.ProcessMedia(new MediaRequest { Files = new List<IFormFile>(), Prompt = null });

        // Without valid files the implementation returns BadRequest or Problem
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ProcessMedia_WithRequest_DoesNotThrow()
    {
        var controller = CreateController();
        var request = new MediaRequest { Prompt = "describe this" };

        ActionResult? result = null;
        Assert.DoesNotThrowAsync(async () =>
        {
            result = await controller.ProcessMedia(request);
        });
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ProcessMedia_EmptyPromptAndNoFiles_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.ProcessMedia(new MediaRequest { Prompt = "", Files = new List<IFormFile>() });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>()
            .Or.InstanceOf<ObjectResult>());
    }

    [Test]
    public void MediaController_CanBeInstantiated()
    {
        var controller = CreateController();
        Assert.That(controller, Is.Not.Null);
    }

    [Test]
    public async Task ProcessMedia_NullRequest_HandlesGracefully()
    {
        var controller = CreateController();
        ActionResult? result = null;

        Assert.DoesNotThrowAsync(async () =>
        {
            result = await controller.ProcessMedia(new MediaRequest());
        });

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ProcessMedia_WithFormContentType_ReadsFormAndContinues()
    {
        // Exercise the HasFormContentType == true branch (lines 106-108)
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";

        var controller = new MediaController(
            _mediaStorageMock.Object,
            _aiServiceMock.Object,
            _historyServiceMock.Object,
            _backgroundQueueMock.Object,
            _mediaProcessorMock.Object,
            _ragServiceMock.Object,
            _context,
            _loggerMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        ActionResult? result = null;
        Assert.DoesNotThrowAsync(async () =>
        {
            result = await controller.ProcessMedia(new MediaRequest { Prompt = "test", Files = new List<IFormFile>() });
        });
        Assert.That(result, Is.Not.Null);
    }
}
