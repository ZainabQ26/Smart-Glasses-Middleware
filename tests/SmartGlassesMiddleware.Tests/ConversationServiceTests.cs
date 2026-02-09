using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class ConversationServiceTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private IConfiguration GetMockConfiguration()
    {
        var mockSection = new Mock<IConfigurationSection>();
        mockSection.Setup(s => s.Value).Returns("0.25");

        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c.GetSection("RAG:ApproximateTokensPerChar"))
            .Returns(mockSection.Object);
        mockConfig.Setup(c => c["RAG:ApproximateTokensPerChar"])
            .Returns("0.25");

        return mockConfig.Object;
    }

    [Fact]
    public async Task CreateConversationAsync_Creates_New_Conversation()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        // Act
        var conversation = await service.CreateConversationAsync("Test Conversation");

        // Assert
        Assert.NotEqual(Guid.Empty, conversation.Id);
        Assert.Equal("Test Conversation", conversation.Title);
        Assert.True(conversation.IsActive);
        Assert.NotEqual(DateTime.MinValue, conversation.CreatedAt);
        Assert.Equal(1, await context.Conversations.CountAsync());
    }

    [Fact]
    public async Task CreateConversationAsync_With_GroupId()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);
        var groupId = Guid.NewGuid();

        // Act
        var conversation = await service.CreateConversationAsync("Test", groupId);

        // Assert
        Assert.Equal(groupId, conversation.ResourceGroupId);
    }

    [Fact]
    public async Task CreateConversationAsync_Generates_Default_Title()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        // Act
        var conversation = await service.CreateConversationAsync();

        // Assert
        Assert.NotNull(conversation.Title);
        Assert.Contains("Conversation", conversation.Title);
    }

    [Fact]
    public async Task GetConversationAsync_Returns_Conversation_With_Messages()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");
        await service.AddMessageAsync(conversation.Id, "user", "Hello");

        // Act
        var retrieved = await service.GetConversationAsync(conversation.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Single(retrieved.Messages);
        Assert.Equal("Hello", retrieved.Messages.First().Content);
    }

    [Fact]
    public async Task GetConversationAsync_Returns_Null_For_NonExistent()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        // Act
        var conversation = await service.GetConversationAsync(Guid.NewGuid());

        // Assert
        Assert.Null(conversation);
    }

    [Fact]
    public async Task GetActiveConversationsAsync_Returns_Only_Active()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var active1 = await service.CreateConversationAsync("Active 1");
        var active2 = await service.CreateConversationAsync("Active 2");
        var inactive = await service.CreateConversationAsync("Inactive");
        await service.ArchiveConversationAsync(inactive.Id);

        // Act
        var activeConversations = await service.GetActiveConversationsAsync();

        // Assert
        Assert.Equal(2, activeConversations.Count);
        Assert.All(activeConversations, c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task GetActiveConversationsAsync_Filters_By_GroupId()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var groupId1 = Guid.NewGuid();
        var groupId2 = Guid.NewGuid();

        await service.CreateConversationAsync("Group1 Conv", groupId1);
        await service.CreateConversationAsync("Group2 Conv", groupId2);

        // Act
        var group1Conversations = await service.GetActiveConversationsAsync(groupId1);

        // Assert
        Assert.Single(group1Conversations);
        Assert.Equal(groupId1, group1Conversations.First().ResourceGroupId);
    }

    [Fact]
    public async Task AddMessageAsync_Adds_Message_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");

        // Act
        var message = await service.AddMessageAsync(conversation.Id, "user", "Hello", "gpt-4");

        // Assert
        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal("user", message.Role);
        Assert.Equal("Hello", message.Content);
        Assert.Equal("gpt-4", message.Model);
        Assert.True(message.TokenCount > 0);
    }

    [Fact]
    public async Task AddMessageAsync_Throws_For_NonExistent_Conversation()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddMessageAsync(Guid.NewGuid(), "user", "Hello"));
    }

    [Fact]
    public async Task AddMessageAsync_Updates_Conversation_Timestamp()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");
        var originalTimestamp = conversation.UpdatedAt;

        await Task.Delay(10);

        // Act
        await service.AddMessageAsync(conversation.Id, "user", "Hello");

        // Assert
        var updated = await context.Conversations.FindAsync(conversation.Id);
        Assert.True(updated!.UpdatedAt > originalTimestamp);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_Returns_Limited_Count()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");
        for (int i = 0; i < 15; i++)
        {
            await service.AddMessageAsync(conversation.Id, "user", $"Message {i}");
        }

        // Act
        var recentMessages = await service.GetRecentMessagesAsync(conversation.Id, 10);

        // Assert
        Assert.Equal(10, recentMessages.Count);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_Returns_In_Chronological_Order()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");
        await service.AddMessageAsync(conversation.Id, "user", "First");
        await Task.Delay(10);
        await service.AddMessageAsync(conversation.Id, "assistant", "Second");
        await Task.Delay(10);
        await service.AddMessageAsync(conversation.Id, "user", "Third");

        // Act
        var messages = await service.GetRecentMessagesAsync(conversation.Id, 10);

        // Assert
        Assert.Equal("First", messages[0].Content);
        Assert.Equal("Second", messages[1].Content);
        Assert.Equal("Third", messages[2].Content);
    }

    [Fact]
    public async Task DetectOrCreateConversationAsync_Creates_New_When_None_Recent()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        // Act
        var conversation = await service.DetectOrCreateConversationAsync("Hello world");

        // Assert
        Assert.NotEqual(Guid.Empty, conversation.Id);
        Assert.Contains("Hello world", conversation.Title);
    }

    [Fact]
    public async Task DetectOrCreateConversationAsync_Reuses_Recent_Conversation()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var existing = await service.CreateConversationAsync("Existing");
        await service.AddMessageAsync(existing.Id, "user", "Hello");

        // Act
        var conversation = await service.DetectOrCreateConversationAsync("Follow up");

        // Assert
        Assert.Equal(existing.Id, conversation.Id);
    }

    [Fact]
    public async Task DetectOrCreateConversationAsync_Truncates_Long_Title()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var longMessage = new string('A', 100);

        // Act
        var conversation = await service.DetectOrCreateConversationAsync(longMessage);

        // Assert
        Assert.True(conversation.Title.Length <= 54); // 50 chars + "..."
        Assert.EndsWith("...", conversation.Title);
    }

    [Fact]
    public async Task SummarizeConversationAsync_Creates_Summary()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");
        for (int i = 0; i < 10; i++)
        {
            await service.AddMessageAsync(conversation.Id, "user", $"Message {i}");
        }

        // Act
        var summary = await service.SummarizeConversationAsync(conversation.Id);

        // Assert
        Assert.NotEmpty(summary);
        Assert.Contains("Conversation started", summary);
        Assert.Contains("Messages: 10", summary);
    }

    [Fact]
    public async Task SummarizeConversationAsync_Handles_Short_Conversation()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");
        await service.AddMessageAsync(conversation.Id, "user", "Hi");

        // Act
        var summary = await service.SummarizeConversationAsync(conversation.Id);

        // Assert
        Assert.Contains("too short", summary);
    }

    [Fact]
    public async Task SummarizeConversationAsync_Throws_For_NonExistent()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SummarizeConversationAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ArchiveConversationAsync_Sets_IsActive_False()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        var conversation = await service.CreateConversationAsync("Test");
        Assert.True(conversation.IsActive);

        // Act
        await service.ArchiveConversationAsync(conversation.Id);

        // Assert
        var archived = await context.Conversations.FindAsync(conversation.Id);
        Assert.False(archived!.IsActive);
    }

    [Fact]
    public async Task ArchiveConversationAsync_Does_Not_Throw_For_NonExistent()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockLogger = new Mock<ILogger<ConversationService>>();
        var config = GetMockConfiguration();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var service = new ConversationService(context, mockLogger.Object, config, provider);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() =>
            service.ArchiveConversationAsync(Guid.NewGuid()));
        Assert.Null(exception);
    }
}
