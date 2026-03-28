using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class GroupConversationMappingsControllerTests
{
    private Mock<IGroupConversationMappingService> _mappingMock;
    private GroupConversationMappingsController _controller;

    [SetUp]
    public void Setup()
    {
        _mappingMock = new Mock<IGroupConversationMappingService>();
        _controller = new GroupConversationMappingsController(_mappingMock.Object);
    }

    [Test]
    public async Task GetMapping_EmptyGuid_ReturnsBadRequest()
    {
        var result = await _controller.GetMapping(Guid.Empty);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetMapping_NotFound_ReturnsNotFound()
    {
        _mappingMock.Setup(x => x.GetConversationIdForGroupAsync(It.IsAny<Guid>())).ReturnsAsync((Guid?)null);
        var result = await _controller.GetMapping(Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetMapping_Found_ReturnsOk()
    {
        var convId = Guid.NewGuid();
        _mappingMock.Setup(x => x.GetConversationIdForGroupAsync(It.IsAny<Guid>())).ReturnsAsync(convId);

        var result = await _controller.GetMapping(Guid.NewGuid()) as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value!.ToString(), Contains.Substring(convId.ToString()));
    }

    [Test]
    public async Task UpsertMapping_InvalidDto_ReturnsBadRequest()
    {
        var result = await _controller.UpsertMapping(new GroupConversationMapping());
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UpsertMapping_ValidDto_ReturnsNoContent()
    {
        var result = await _controller.UpsertMapping(new GroupConversationMapping { ResourceGroupId = Guid.NewGuid(), ConversationId = Guid.NewGuid() });
        Assert.That(result, Is.InstanceOf<NoContentResult>());
        _mappingMock.Verify(x => x.UpsertMappingAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Once);
    }

    [Test]
    public async Task DeleteMapping_EmptyGuid_ReturnsBadRequest()
    {
        var result = await _controller.DeleteMapping(Guid.Empty);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task DeleteMapping_ValidGuid_ReturnsNoContent()
    {
        var result = await _controller.DeleteMapping(Guid.NewGuid());
        Assert.That(result, Is.InstanceOf<NoContentResult>());
        _mappingMock.Verify(x => x.DeleteMappingAsync(It.IsAny<Guid>()), Times.Once);
    }
}
