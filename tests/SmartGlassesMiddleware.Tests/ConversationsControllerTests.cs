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
public class ConversationsControllerTests
{
    private ApplicationDbContext _context = null!;
    private Mock<ILogger<ConversationsController>> _loggerMock = null!;
    private Mock<IConversationService> _conversationMock = null!;
    private Mock<ICacheService> _cacheMock = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _loggerMock = new Mock<ILogger<ConversationsController>>();
        _conversationMock = new Mock<IConversationService>();
        _cacheMock = new Mock<ICacheService>();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    private ConversationsController CreateController()
    {
        var controller = new ConversationsController(
            _loggerMock.Object,
            _conversationMock.Object,
            _context,
            _cacheMock.Object,
            resourceHub: null);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    // ─── CreateConversation ──────────────────────────────────────────────────

    [Test]
    public async Task CreateConversation_ValidRequest_ReturnsCreated()
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Test Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _conversationMock
            .Setup(c => c.CreateConversationAsync(It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(conversation);

        var controller = CreateController();
        var request = new CreateConversationRequest { Title = "Test Conversation" };

        var result = await controller.CreateConversation(request);

        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
        var created = (CreatedAtActionResult)result;
        Assert.That(created.StatusCode, Is.EqualTo(201));
    }

    [Test]
    public async Task CreateConversation_ServiceThrows_Returns500()
    {
        _conversationMock
            .Setup(c => c.CreateConversationAsync(It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController();
        var request = new CreateConversationRequest { Title = "Failing Conversation" };

        var result = await controller.CreateConversation(request);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var obj = (ObjectResult)result;
        Assert.That(obj.StatusCode, Is.EqualTo(500));
    }

    // ─── GetConversations ────────────────────────────────────────────────────

    [Test]
    public async Task GetConversations_EmptyDb_ReturnsOkWithEmptyItems()
    {
        var controller = CreateController();

        var result = await controller.GetConversations();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetConversations_WithActiveConversation_ReturnsOk()
    {
        _context.Conversations.Add(new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Active Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetConversations();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetConversations_InactiveConversation_NotReturned()
    {
        // IsActive=false conversations are filtered out
        _context.Conversations.Add(new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Inactive Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = false
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetConversations();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        // totalCount should be 0 because the inactive conversation is filtered out
        Assert.That(json, Does.Contain("\"totalCount\":0"));
    }

    // ─── GetConversation ─────────────────────────────────────────────────────

    [Test]
    public async Task GetConversation_NotInDb_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.GetConversation(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetConversation_InDb_ReturnsOk()
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "My Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetConversation(conversation.Id);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    // ─── UpdateConversation ──────────────────────────────────────────────────

    [Test]
    public async Task UpdateConversation_NullTitle_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new UpdateConversationRequest { Title = null };

        var result = await controller.UpdateConversation(Guid.NewGuid(), request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UpdateConversation_NotFound_Returns404()
    {
        var controller = CreateController();
        var request = new UpdateConversationRequest { Title = "New Title" };

        var result = await controller.UpdateConversation(Guid.NewGuid(), request);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task UpdateConversation_Found_UpdatesTitleAndReturnsOk()
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Old Title",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var request = new UpdateConversationRequest { Title = "New Title" };

        var result = await controller.UpdateConversation(conversation.Id, request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var updated = await _context.Conversations.FindAsync(conversation.Id);
        Assert.That(updated!.Title, Is.EqualTo("New Title"));
    }
}
