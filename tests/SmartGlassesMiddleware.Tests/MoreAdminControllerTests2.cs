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
public class MoreAdminControllerTests2
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

        _cacheMock.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);
        _cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        _monitoringMock.Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _monitoringMock.Setup(m => m.CompleteRequest(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _monitoringMock.Setup(m => m.ClearAll()).Returns(Task.CompletedTask);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

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
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    // ── UpdateGroup ──────────────────────────────────────────────────────────

    [Test]
    public async Task UpdateGroup_NotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.UpdateGroup(Guid.NewGuid(), new GroupRequest { Name = "X" });
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task UpdateGroup_EmptyName_ReturnsBadRequest()
    {
        var group = new ResourceGroup { Id = Guid.NewGuid(), Name = "Original", CreatedAt = DateTime.UtcNow };
        _context.ResourceGroups.Add(group);
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.UpdateGroup(group.Id, new GroupRequest { Name = "" });
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UpdateGroup_ValidRequest_ReturnsOk()
    {
        var group = new ResourceGroup { Id = Guid.NewGuid(), Name = "OldName", CreatedAt = DateTime.UtcNow };
        _context.ResourceGroups.Add(group);
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.UpdateGroup(group.Id, new GroupRequest { Name = "NewName" });
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    // ── UpdatePrompt ─────────────────────────────────────────────────────────

    [Test]
    public async Task UpdatePrompt_NotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.UpdatePrompt(Guid.NewGuid(), new PromptRequest { Name = "X", PromptText = "Y" });
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task UpdatePrompt_ValidRequest_ReturnsOk()
    {
        var prompt = new ManagedPrompt { Id = Guid.NewGuid(), Name = "Old", PromptText = "OldText", CreatedAt = DateTime.UtcNow };
        _context.ManagedPrompts.Add(prompt);
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.UpdatePrompt(prompt.Id, new PromptRequest { Name = "New", PromptText = "NewText" });
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    // ── DeletePrompt ─────────────────────────────────────────────────────────

    [Test]
    public async Task DeletePrompt_NotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.DeletePrompt(Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task DeletePrompt_ExistingPrompt_ReturnsNoContent()
    {
        var prompt = new ManagedPrompt { Id = Guid.NewGuid(), Name = "ToDelete", PromptText = "Text", CreatedAt = DateTime.UtcNow };
        _context.ManagedPrompts.Add(prompt);
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.DeletePrompt(prompt.Id);
        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(await _context.ManagedPrompts.CountAsync(), Is.EqualTo(0));
    }

    // ── AddFileToGroup ───────────────────────────────────────────────────────

    [Test]
    public async Task AddFileToGroup_GroupNotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.AddFileToGroup(Guid.NewGuid(), Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task AddFileToGroup_FileNotFound_Returns404()
    {
        var group = new ResourceGroup { Id = Guid.NewGuid(), Name = "G", CreatedAt = DateTime.UtcNow };
        _context.ResourceGroups.Add(group);
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.AddFileToGroup(group.Id, Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task AddFileToGroup_ValidIds_ReturnsOk()
    {
        var group = new ResourceGroup { Id = Guid.NewGuid(), Name = "G", CreatedAt = DateTime.UtcNow };
        var file = new ManagedFile
        {
            Id = Guid.NewGuid(), FileName = "f.pdf", Label = "F",
            ContentType = "application/pdf", StorageKey = "k", UploadedAt = DateTime.UtcNow, FileSize = 1
        };
        _context.ResourceGroups.Add(group);
        _context.ManagedFiles.Add(file);
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.AddFileToGroup(group.Id, file.Id);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    // ── AddPromptToGroup ─────────────────────────────────────────────────────

    [Test]
    public async Task AddPromptToGroup_GroupNotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.AddPromptToGroup(Guid.NewGuid(), Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    // ── RemoveFileFromGroup ───────────────────────────────────────────────────

    [Test]
    public async Task RemoveFileFromGroup_NotInGroup_Returns404()
    {
        var controller = CreateController();
        var result = await controller.RemoveFileFromGroup(Guid.NewGuid(), Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    // ── ClearAll ─────────────────────────────────────────────────────────────

    [Test]
    public async Task ClearAll_EmptyDb_ReturnsOk()
    {
        var controller = CreateController();
        var result = await controller.ClearAll();
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    // ── SetPreventSleepSetting ────────────────────────────────────────────────

    [Test]
    public async Task SetPreventSleepSetting_ReturnsOk()
    {
        _settingsMock.Setup(s => s.SetPreventSleepEnabledAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreventSleepSettingState(true, false, DateTime.UtcNow));

        var controller = CreateController();
        var result = await controller.SetPreventSleepSetting(
            new PreventSleepSettingRequest { Enabled = true }, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }
}
