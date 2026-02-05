using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class ConversationsControllerTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private ConversationsController CreateController(
        ApplicationDbContext context,
        Mock<IConversationService>? conversationService = null,
        Mock<IRAGService>? ragService = null)
    {
        var mockConversationService = conversationService ?? new Mock<IConversationService>();
        var mockRagService = ragService ?? new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<ConversationsController>>();

        var controller = new ConversationsController(
            mockLogger.Object,
            mockConversationService.Object,
            mockRagService.Object,
            context);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task CreateConversation_Returns_Created_With_Conversation()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Test Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        mockConversationService.Setup(s => s.CreateConversationAsync(
            It.IsAny<string?>(),
            It.IsAny<Guid?>()))
            .ReturnsAsync(conversation);

        var controller = CreateController(context, mockConversationService);

        var request = new CreateConversationRequest
        {
            Title = "Test Conversation"
        };

        // Act
        var result = await controller.CreateConversation(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ConversationsController.GetConversation), createdResult.ActionName);
        var returnedConversation = Assert.IsType<Conversation>(createdResult.Value);
        Assert.Equal(conversation.Id, returnedConversation.Id);
    }

    [Fact]
    public async Task CreateConversation_With_ResourceGroupId()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var groupId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Test Conversation",
            ResourceGroupId = groupId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        mockConversationService.Setup(s => s.CreateConversationAsync(
            It.IsAny<string?>(),
            groupId))
            .ReturnsAsync(conversation);

        var controller = CreateController(context, mockConversationService);

        var request = new CreateConversationRequest
        {
            Title = "Test Conversation",
            ResourceGroupId = groupId
        };

        // Act
        var result = await controller.CreateConversation(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        mockConversationService.Verify(s => s.CreateConversationAsync(
            "Test Conversation",
            groupId), Times.Once);
    }

    [Fact]
    public async Task CreateConversation_Returns_500_When_Service_Throws()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        mockConversationService.Setup(s => s.CreateConversationAsync(
            It.IsAny<string?>(),
            It.IsAny<Guid?>()))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(context, mockConversationService);

        var request = new CreateConversationRequest
        {
            Title = "Test Conversation"
        };

        // Act
        var result = await controller.CreateConversation(request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task GetConversation_Returns_Ok_When_Conversation_Exists()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversationId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = conversationId,
            Title = "Test Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        mockConversationService.Setup(s => s.GetConversationAsync(conversationId))
            .ReturnsAsync(conversation);

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.GetConversation(conversationId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedConversation = Assert.IsType<Conversation>(okResult.Value);
        Assert.Equal(conversationId, returnedConversation.Id);
    }

    [Fact]
    public async Task GetConversation_Returns_NotFound_When_Conversation_Does_Not_Exist()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        mockConversationService.Setup(s => s.GetConversationAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Conversation?)null);

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.GetConversation(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetConversations_Returns_Ok_With_Conversations()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversations = new List<Conversation>
        {
            new Conversation
            {
                Id = Guid.NewGuid(),
                Title = "Conversation 1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            },
            new Conversation
            {
                Id = Guid.NewGuid(),
                Title = "Conversation 2",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            }
        };

        mockConversationService.Setup(s => s.GetActiveConversationsAsync(null))
            .ReturnsAsync(conversations);

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.GetConversations();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedConversations = Assert.IsAssignableFrom<List<Conversation>>(okResult.Value);
        Assert.Equal(2, returnedConversations.Count);
    }

    [Fact]
    public async Task GetConversations_Filters_By_GroupId()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var groupId = Guid.NewGuid();
        var conversations = new List<Conversation>
        {
            new Conversation
            {
                Id = Guid.NewGuid(),
                Title = "Conversation 1",
                ResourceGroupId = groupId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            }
        };

        mockConversationService.Setup(s => s.GetActiveConversationsAsync(groupId))
            .ReturnsAsync(conversations);

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.GetConversations(groupId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockConversationService.Verify(s => s.GetActiveConversationsAsync(groupId), Times.Once);
    }

    [Fact]
    public async Task GetConversations_Returns_Empty_List_When_No_Conversations()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        mockConversationService.Setup(s => s.GetActiveConversationsAsync(null))
            .ReturnsAsync(new List<Conversation>());

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.GetConversations();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedConversations = Assert.IsAssignableFrom<List<Conversation>>(okResult.Value);
        Assert.Empty(returnedConversations);
    }

    [Fact]
    public async Task AddMessage_Returns_Created_With_Message()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversationId = Guid.NewGuid();
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = "Hello",
            Timestamp = DateTime.UtcNow
        };

        mockConversationService.Setup(s => s.AddMessageAsync(
            conversationId,
            "user",
            "Hello",
            null))
            .ReturnsAsync(message);

        var controller = CreateController(context, mockConversationService);

        var request = new AddMessageRequest
        {
            Role = "user",
            Content = "Hello"
        };

        // Act
        var result = await controller.AddMessage(conversationId, request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returnedMessage = Assert.IsType<ChatMessage>(createdResult.Value);
        Assert.Equal(message.Id, returnedMessage.Id);
    }

    [Fact]
    public async Task AddMessage_With_Model()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversationId = Guid.NewGuid();
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            Content = "Hello back",
            Model = "gpt-4",
            Timestamp = DateTime.UtcNow
        };

        mockConversationService.Setup(s => s.AddMessageAsync(
            conversationId,
            "assistant",
            "Hello back",
            "gpt-4"))
            .ReturnsAsync(message);

        var controller = CreateController(context, mockConversationService);

        var request = new AddMessageRequest
        {
            Role = "assistant",
            Content = "Hello back",
            Model = "gpt-4"
        };

        // Act
        var result = await controller.AddMessage(conversationId, request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        mockConversationService.Verify(s => s.AddMessageAsync(
            conversationId,
            "assistant",
            "Hello back",
            "gpt-4"), Times.Once);
    }

    [Fact]
    public async Task AddMessage_Returns_NotFound_When_Conversation_Does_Not_Exist()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        mockConversationService.Setup(s => s.AddMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .ThrowsAsync(new ArgumentException("Conversation not found"));

        var controller = CreateController(context, mockConversationService);

        var request = new AddMessageRequest
        {
            Role = "user",
            Content = "Hello"
        };

        // Act
        var result = await controller.AddMessage(Guid.NewGuid(), request);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AddMessage_Returns_500_When_Service_Throws_Exception()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        mockConversationService.Setup(s => s.AddMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .ThrowsAsync(new Exception("Database error"));

        var controller = CreateController(context, mockConversationService);

        var request = new AddMessageRequest
        {
            Role = "user",
            Content = "Hello"
        };

        // Act
        var result = await controller.AddMessage(Guid.NewGuid(), request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task QueryWithConversation_Returns_Ok_With_Result()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        var conversationId = Guid.NewGuid();
        var queryResult = new QueryResult
        {
            Answer = "Test answer",
            Sources = new List<DocumentSource>()
        };

        mockConversationService.Setup(s => s.AddMessageAsync(
            conversationId,
            "user",
            "Test query",
            null))
            .ReturnsAsync(new ChatMessage());

        mockRagService.Setup(s => s.QueryDocumentsWithConversationAsync(
            "Test query",
            conversationId,
            5,
            null))
            .ReturnsAsync(queryResult);

        mockConversationService.Setup(s => s.AddMessageAsync(
            conversationId,
            "assistant",
            "Test answer",
            "default"))
            .ReturnsAsync(new ChatMessage());

        var controller = CreateController(context, mockConversationService, mockRagService);

        var request = new ConversationQueryRequest
        {
            Query = "Test query"
        };

        // Act
        var result = await controller.QueryWithConversation(conversationId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResult = Assert.IsType<QueryResult>(okResult.Value);
        Assert.Equal("Test answer", returnedResult.Answer);
    }

    [Fact]
    public async Task QueryWithConversation_Returns_BadRequest_When_Query_Is_Empty()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);

        var request = new ConversationQueryRequest
        {
            Query = string.Empty
        };

        // Act
        var result = await controller.QueryWithConversation(Guid.NewGuid(), request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task QueryWithConversation_Returns_BadRequest_When_Query_Is_Whitespace()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);

        var request = new ConversationQueryRequest
        {
            Query = "   "
        };

        // Act
        var result = await controller.QueryWithConversation(Guid.NewGuid(), request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task QueryWithConversation_Uses_Custom_TopK()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        var conversationId = Guid.NewGuid();
        var queryResult = new QueryResult
        {
            Answer = "Test answer",
            Sources = new List<DocumentSource>()
        };

        mockConversationService.Setup(s => s.AddMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .ReturnsAsync(new ChatMessage());

        mockRagService.Setup(s => s.QueryDocumentsWithConversationAsync(
            "Test query",
            conversationId,
            10,
            null))
            .ReturnsAsync(queryResult);

        var controller = CreateController(context, mockConversationService, mockRagService);

        var request = new ConversationQueryRequest
        {
            Query = "Test query",
            TopK = 10
        };

        // Act
        var result = await controller.QueryWithConversation(conversationId, request);

        // Assert
        mockRagService.Verify(s => s.QueryDocumentsWithConversationAsync(
            "Test query",
            conversationId,
            10,
            null), Times.Once);
    }

    [Fact]
    public async Task QueryWithConversation_Uses_GroupIds()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        var conversationId = Guid.NewGuid();
        var groupIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var queryResult = new QueryResult
        {
            Answer = "Test answer",
            Sources = new List<DocumentSource>()
        };

        mockConversationService.Setup(s => s.AddMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .ReturnsAsync(new ChatMessage());

        mockRagService.Setup(s => s.QueryDocumentsWithConversationAsync(
            "Test query",
            conversationId,
            5,
            groupIds))
            .ReturnsAsync(queryResult);

        var controller = CreateController(context, mockConversationService, mockRagService);

        var request = new ConversationQueryRequest
        {
            Query = "Test query",
            GroupIds = groupIds
        };

        // Act
        var result = await controller.QueryWithConversation(conversationId, request);

        // Assert
        mockRagService.Verify(s => s.QueryDocumentsWithConversationAsync(
            "Test query",
            conversationId,
            5,
            groupIds), Times.Once);
    }

    [Fact]
    public async Task QueryWithConversation_Returns_500_When_Service_Throws()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        mockConversationService.Setup(s => s.AddMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .ThrowsAsync(new Exception("Service error"));

        var controller = CreateController(context, mockConversationService, mockRagService);

        var request = new ConversationQueryRequest
        {
            Query = "Test query"
        };

        // Act
        var result = await controller.QueryWithConversation(Guid.NewGuid(), request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task SummarizeConversation_Returns_Ok_With_Summary()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversationId = Guid.NewGuid();
        var summary = "This is a summary";

        mockConversationService.Setup(s => s.SummarizeConversationAsync(conversationId))
            .ReturnsAsync(summary);

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.SummarizeConversation(conversationId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task SummarizeConversation_Generates_Embedding_When_Requested()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        var conversationId = Guid.NewGuid();
        var summary = "This is a summary";
        var embeddingJson = "[0.1, 0.2, 0.3]";
        var dimension = 3;

        var conversation = new Conversation
        {
            Id = conversationId,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();

        mockConversationService.Setup(s => s.SummarizeConversationAsync(conversationId))
            .ReturnsAsync(summary);

        mockRagService.Setup(s => s.GenerateTextEmbeddingJsonAsync(summary))
            .ReturnsAsync((embeddingJson, dimension));

        var controller = CreateController(context, mockConversationService, mockRagService);

        // Act
        var result = await controller.SummarizeConversation(conversationId, generateEmbedding: true);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockRagService.Verify(s => s.GenerateTextEmbeddingJsonAsync(summary), Times.Once);

        var updatedConversation = await context.Conversations.FindAsync(conversationId);
        Assert.NotNull(updatedConversation);
        Assert.Equal(embeddingJson, updatedConversation.SummaryEmbedding);
        Assert.Equal(dimension, updatedConversation.SummaryEmbeddingDimension);
    }

    [Fact]
    public async Task SummarizeConversation_Does_Not_Generate_Embedding_By_Default()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        var conversationId = Guid.NewGuid();
        var summary = "This is a summary";

        mockConversationService.Setup(s => s.SummarizeConversationAsync(conversationId))
            .ReturnsAsync(summary);

        var controller = CreateController(context, mockConversationService, mockRagService);

        // Act
        var result = await controller.SummarizeConversation(conversationId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockRagService.Verify(s => s.GenerateTextEmbeddingJsonAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SummarizeConversation_Returns_NotFound_When_Conversation_Does_Not_Exist()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        mockConversationService.Setup(s => s.SummarizeConversationAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new ArgumentException("Conversation not found"));

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.SummarizeConversation(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SummarizeConversation_Returns_500_When_Service_Throws()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        mockConversationService.Setup(s => s.SummarizeConversationAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new Exception("Service error"));

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.SummarizeConversation(Guid.NewGuid());

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task ArchiveConversation_Returns_NoContent()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversationId = Guid.NewGuid();

        mockConversationService.Setup(s => s.ArchiveConversationAsync(conversationId))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockConversationService);

        // Act
        var result = await controller.ArchiveConversation(conversationId);

        // Assert
        Assert.IsType<NoContentResult>(result);
        mockConversationService.Verify(s => s.ArchiveConversationAsync(conversationId), Times.Once);
    }

    [Fact]
    public async Task ArchiveConversation_Calls_Service()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();

        var conversationId = Guid.NewGuid();

        mockConversationService.Setup(s => s.ArchiveConversationAsync(conversationId))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockConversationService);

        // Act
        await controller.ArchiveConversation(conversationId);

        // Assert
        mockConversationService.Verify(s => s.ArchiveConversationAsync(conversationId), Times.Once);
    }

    [Fact]
    public async Task QueryWithConversation_Uses_Custom_Model()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        var conversationId = Guid.NewGuid();
        var queryResult = new QueryResult
        {
            Answer = "Test answer",
            Sources = new List<DocumentSource>()
        };

        mockConversationService.Setup(s => s.AddMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()))
            .ReturnsAsync(new ChatMessage());

        mockRagService.Setup(s => s.QueryDocumentsWithConversationAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<int>(),
            It.IsAny<List<Guid>?>()))
            .ReturnsAsync(queryResult);

        var controller = CreateController(context, mockConversationService, mockRagService);

        var request = new ConversationQueryRequest
        {
            Query = "Test query",
            Model = "gpt-4"
        };

        // Act
        var result = await controller.QueryWithConversation(conversationId, request);

        // Assert
        mockConversationService.Verify(s => s.AddMessageAsync(
            conversationId,
            "assistant",
            "Test answer",
            "gpt-4"), Times.Once);
    }

    [Fact]
    public async Task SummarizeConversation_Skips_Embedding_When_Summary_Is_Empty()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockConversationService = new Mock<IConversationService>();
        var mockRagService = new Mock<IRAGService>();

        var conversationId = Guid.NewGuid();

        mockConversationService.Setup(s => s.SummarizeConversationAsync(conversationId))
            .ReturnsAsync(string.Empty);

        var controller = CreateController(context, mockConversationService, mockRagService);

        // Act
        var result = await controller.SummarizeConversation(conversationId, generateEmbedding: true);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockRagService.Verify(s => s.GenerateTextEmbeddingJsonAsync(It.IsAny<string>()), Times.Never);
    }
}
