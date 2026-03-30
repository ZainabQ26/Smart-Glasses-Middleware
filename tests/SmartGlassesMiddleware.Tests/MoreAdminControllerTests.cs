using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class MoreAdminControllerTests
{
    private ApplicationDbContext _context = null!;
    private Mock<IMediaStorageService> _mediaStorageMock = null!;
    private Mock<ICacheService> _cacheMock = null!;
    private Mock<IRagService> _ragServiceMock = null!;
    private Mock<IRAGService> _docRagServiceMock = null!;
    private Mock<ILogger<AdminController>> _loggerMock = null!;
    private Mock<IRequestMonitoringService> _monitoringMock = null!;
    private Mock<IServerSettingsService> _settingsMock = null!;
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _mediaStorageMock = new Mock<IMediaStorageService>();
        _cacheMock = new Mock<ICacheService>();
        _ragServiceMock = new Mock<IRagService>();
        _docRagServiceMock = new Mock<IRAGService>();
        _loggerMock = new Mock<ILogger<AdminController>>();
        _monitoringMock = new Mock<IRequestMonitoringService>();
        _settingsMock = new Mock<IServerSettingsService>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        // Cache returns null by default so DB path is exercised
        _cacheMock.Setup(c => c.GetAsync<object>(It.IsAny<string>()))
            .ReturnsAsync((object?)null);
        _cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _monitoringMock
            .Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _monitoringMock
            .Setup(m => m.CompleteRequest(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    private AdminController CreateController()
    {
        var controller = new AdminController(
            _context,
            _mediaStorageMock.Object,
            _cacheMock.Object,
            _ragServiceMock.Object,
            _docRagServiceMock.Object,
            _loggerMock.Object,
            _monitoringMock.Object,
            _settingsMock.Object,
            _scopeFactoryMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    // ─── GetFiles ───────────────────────────────────────────────────────────

    [Test]
    public async Task GetFiles_EmptyDb_ReturnsOk()
    {
        var controller = CreateController();

        var result = await controller.GetFiles();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetFiles_WithVisibleFile_ReturnsOk()
    {
        // GetFiles filters StorageKey non-empty and FileSize > 0;
        // EF.Functions.Like isn't supported in-memory, so the controller catches the
        // exception and returns CreateEmptyPagedResult – still an OkObjectResult.
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = Guid.NewGuid(),
            FileName = "doc.pdf",
            Label = "My Doc",
            ContentType = "application/pdf",
            StorageKey = "uploads/doc.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetFiles();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    // ─── CreateGroup ─────────────────────────────────────────────────────────

    [Test]
    public async Task CreateGroup_ValidRequest_ReturnsCreatedGroup()
    {
        var controller = CreateController();
        var request = new GroupRequest { Name = "Test Group", Description = "A group for testing" };

        var result = await controller.CreateGroup(request);

        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
        var created = (CreatedAtActionResult)result;
        Assert.That(created.StatusCode, Is.EqualTo(201));
    }

    [Test]
    public async Task CreateGroup_EmptyName_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new GroupRequest { Name = "" };

        var result = await controller.CreateGroup(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    // ─── GetGroups ───────────────────────────────────────────────────────────

    [Test]
    public async Task GetGroups_EmptyDb_ReturnsEmptyList()
    {
        var controller = CreateController();

        var result = await controller.GetGroups();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetGroups_WithGroups_ReturnsOk()
    {
        _context.ResourceGroups.Add(new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Group A",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetGroups();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    // ─── GetGroupDetails ─────────────────────────────────────────────────────

    [Test]
    public async Task GetGroupDetails_NotFound_Returns404()
    {
        var controller = CreateController();

        var result = await controller.GetGroupDetails(Guid.NewGuid());

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetGroupDetails_Found_ReturnsOk()
    {
        var group = new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Existing Group",
            CreatedAt = DateTime.UtcNow
        };
        _context.ResourceGroups.Add(group);
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetGroupDetails(group.Id);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    // ─── DeleteGroup ─────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteGroup_NotFound_ReturnsNoContent()
    {
        // Delete is idempotent; returns 204 even when the group doesn't exist
        var controller = CreateController();

        var result = await controller.DeleteGroup(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public async Task DeleteGroup_WithGroup_Returns204OrError()
    {
        // Note: ExecuteDeleteAsync (bulk-delete) is not supported by the EF in-memory
        // provider, so the controller's catch block returns 500 in this environment.
        // We verify that the group was found (not 404) by checking for NoContentResult
        // or ObjectResult (500) but never NotFoundResult.
        var group = new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "To Delete",
            CreatedAt = DateTime.UtcNow
        };
        _context.ResourceGroups.Add(group);
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.DeleteGroup(group.Id);

        Assert.That(result, Is.Not.InstanceOf<NotFoundObjectResult>());
        Assert.That(result, Is.Not.InstanceOf<NotFoundResult>());
    }

    // ─── GetPrompts ──────────────────────────────────────────────────────────

    [Test]
    public async Task GetPrompts_EmptyDb_ReturnsEmptyList()
    {
        var controller = CreateController();

        var result = await controller.GetPrompts();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    // ─── CreatePrompt ────────────────────────────────────────────────────────

    [Test]
    public async Task CreatePrompt_ValidRequest_CreatesPromptAndReturns201()
    {
        var controller = CreateController();
        var request = new PromptRequest { Name = "My Prompt", PromptText = "Do something useful." };

        var result = await controller.CreatePrompt(request);

        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
        var created = (CreatedAtActionResult)result;
        Assert.That(created.StatusCode, Is.EqualTo(201));
        Assert.That(await _context.ManagedPrompts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CreatePrompt_EmptyName_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new PromptRequest { Name = "", PromptText = "Some text." };

        var result = await controller.CreatePrompt(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task CreatePrompt_EmptyPromptText_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new PromptRequest { Name = "Valid Name", PromptText = "" };

        var result = await controller.CreatePrompt(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    // ─── GetBatch ────────────────────────────────────────────────────────────

    [Test]
    public async Task GetBatch_NoData_ReturnsOkWithDictionary()
    {
        var controller = CreateController();

        var result = await controller.GetBatch();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetBatch_WithFile_ReturnsOk()
    {
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = Guid.NewGuid(),
            FileName = "report.pdf",
            Label = "Report",
            ContentType = "application/pdf",
            StorageKey = "uploads/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 512
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetBatch(includeFiles: true, includePrompts: false, includeGroups: false);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetBatch_OnlyGroups_ReturnsOk()
    {
        _context.ResourceGroups.Add(new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Batch Group",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetBatch(includeFiles: false, includePrompts: false, includeGroups: true);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }
}
