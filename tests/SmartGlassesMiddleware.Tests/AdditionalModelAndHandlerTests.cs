using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

public class ConversationModelTests
{
    [Fact]
    public void Conversation_DefaultValues_ShouldBeSet()
    {
        // Act
        var conversation = new Conversation();

        // Assert
        Assert.NotNull(conversation.Messages);
        Assert.Empty(conversation.Messages);
        Assert.True(conversation.CreatedAt <= DateTime.UtcNow);
        Assert.True(conversation.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Conversation_WithMessages_ShouldStoreMessages()
    {
        // Arrange
        var conversation = new Conversation
        {
            Title = "Test Conversation"
        };
        
        conversation.Messages.Add(new ChatMessage
        {
            Role = "user",
            Content = "Hello"
        });
        
        conversation.Messages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = "Hi there!"
        });

        // Assert
        Assert.Equal("Test Conversation", conversation.Title);
        Assert.Equal(2, conversation.Messages.Count);
        Assert.Equal("Hello", conversation.Messages[0].Content);
        Assert.Equal("Hi there!", conversation.Messages[1].Content);
    }

    [Fact]
    public void ChatMessage_Properties_ShouldPersist()
    {
        // Arrange
        var message = new ChatMessage
        {
            Role = "user",
            Content = "Test message",
            Timestamp = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("user", message.Role);
        Assert.Equal("Test message", message.Content);
        Assert.True(message.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void RagEntry_DefaultValues_ShouldBeSet()
    {
        // Act
        var entry = new RagEntry();

        // Assert
        Assert.Equal(string.Empty, entry.FileName);
        Assert.Equal(string.Empty, entry.IndexedContent);
    }

    [Fact]
    public void RagEntry_WithAllFields_ShouldPersist()
    {
        // Arrange
        var entry = new RagEntry
        {
            FileName = "document.pdf",
            ContentType = "application/pdf",
            StorageKey = "storage-key-123",
            IndexedContent = "Indexed text content",
            EmbeddingVector = new float[] { 0.1f, 0.2f, 0.3f }
        };

        // Assert
        Assert.Equal("document.pdf", entry.FileName);
        Assert.Equal("application/pdf", entry.ContentType);
        Assert.Equal("storage-key-123", entry.StorageKey);
        Assert.Equal("Indexed text content", entry.IndexedContent);
        Assert.NotNull(entry.EmbeddingVector);
        Assert.Equal(3, entry.EmbeddingVector.Length);
    }

    [Fact]
    public void AIInteractionLog_Properties_ShouldPersist()
    {
        // Arrange
        var log = new AIInteractionLog
        {
            Input = "User input",
            Output = "AI output",
            ModelUsed = "gpt-4",
            TotalTokens = 150,
            Timestamp = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("User input", log.Input);
        Assert.Equal("AI output", log.Output);
        Assert.Equal("gpt-4", log.ModelUsed);
        Assert.Equal(150, log.TotalTokens);
    }
}

public class ServiceHandlerEdgeCaseTests
{
    [Fact]
    public void DeleteReminderTaskHandler_HasCorrectName()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
        var handler = new DeleteReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Assert
        Assert.Equal("delete-reminder", handler.Name);
    }

    [Fact]
    public async Task DeleteReminderTaskHandler_WithEmptyInput_ShouldReturnError()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
        var handler = new DeleteReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("");

        // Assert
        Assert.Contains("didn't catch", result.ToLower());
    }

    [Fact]
    public void EditReminderTaskHandler_HasCorrectName()
    {
        // Arrange
        var mockHistoryService = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<EditReminderTaskHandler>>();
        var handler = new EditReminderTaskHandler(mockHistoryService.Object, mockLogger.Object);

        // Assert
        Assert.Equal("edit-reminder", handler.Name);
    }

    [Fact]
    public async Task OCRRetryTaskHandler_WithValidDocumentId_ShouldReprocess()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var mockProcessingService = new Mock<IDocumentProcessingService>();
        var mockRagService = new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<OCRRetryTaskHandler>>();

        var docId = Guid.NewGuid();
        var metadata = new DocumentMetadata
        {
            Id = docId,
            FileName = "test.pdf",
            ProcessedAt = DateTime.UtcNow
        };
        metadata.Chunks.Add(new DocumentChunk { ChunkText = "test", ChunkIndex = 0, PageNumber = 1 });

        mockProcessingService.Setup(p => p.ReprocessDocumentAsync(docId))
            .ReturnsAsync(metadata);
        mockRagService.Setup(r => r.GenerateEmbeddingsAsync(docId))
            .Returns(Task.CompletedTask);

        var handler = new OCRRetryTaskHandler(mockLogger.Object, mockProcessingService.Object, mockRagService.Object);

        // Act
        var result = await handler.HandleAsync(docId.ToString());

        // Assert
        Assert.Contains("ocr", result.ToLower());
        mockProcessingService.Verify(p => p.ReprocessDocumentAsync(docId), Times.Once);
    }

    [Fact]
    public async Task OCRRetryTaskHandler_WithException_ShouldReturnError()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var mockProcessingService = new Mock<IDocumentProcessingService>();
        var mockRagService = new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<OCRRetryTaskHandler>>();

        var docId = Guid.NewGuid();
        mockProcessingService.Setup(p => p.ReprocessDocumentAsync(docId))
            .ThrowsAsync(new Exception("Processing failed"));

        var handler = new OCRRetryTaskHandler(mockLogger.Object, mockProcessingService.Object, mockRagService.Object);

        // Act
        var result = await handler.HandleAsync(docId.ToString());

        // Assert
        Assert.Contains("error", result.ToLower());
    }
}
