using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class GroupConversationMappingServiceTests
{
    private ApplicationDbContext _dbContext;
    private GroupConversationMappingService _service;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
        _service = new GroupConversationMappingService(_dbContext);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task GetConversationIdForGroupAsync_Found_ReturnsId()
    {
        var groupId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        _dbContext.GroupConversationMappings.Add(new GroupConversationMapping { ResourceGroupId = groupId, ConversationId = convId });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetConversationIdForGroupAsync(groupId);
        Assert.That(result, Is.EqualTo(convId));
    }

    [Test]
    public async Task UpsertMappingAsync_New_AddsMapping()
    {
        var groupId = Guid.NewGuid();
        var convId = Guid.NewGuid();

        await _service.UpsertMappingAsync(groupId, convId);

        var mapping = await _dbContext.GroupConversationMappings.FirstOrDefaultAsync(m => m.ResourceGroupId == groupId);
        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ConversationId, Is.EqualTo(convId));
    }

    [Test]
    public async Task UpsertMappingAsync_Existing_UpdatesMapping()
    {
        var groupId = Guid.NewGuid();
        var initialConvId = Guid.NewGuid();
        var newConvId = Guid.NewGuid();

        _dbContext.GroupConversationMappings.Add(new GroupConversationMapping { ResourceGroupId = groupId, ConversationId = initialConvId });
        await _dbContext.SaveChangesAsync();

        await _service.UpsertMappingAsync(groupId, newConvId);

        var mapping = await _dbContext.GroupConversationMappings.FirstOrDefaultAsync(m => m.ResourceGroupId == groupId);
        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping!.ConversationId, Is.EqualTo(newConvId));
    }

    [Test]
    public async Task DeleteMappingAsync_Works()
    {
        var groupId = Guid.NewGuid();
        _dbContext.GroupConversationMappings.Add(new GroupConversationMapping { ResourceGroupId = groupId, ConversationId = Guid.NewGuid() });
        await _dbContext.SaveChangesAsync();

        await _service.DeleteMappingAsync(groupId);

        var mapping = await _dbContext.GroupConversationMappings.FirstOrDefaultAsync(m => m.ResourceGroupId == groupId);
        Assert.That(mapping, Is.Null);
    }
}
