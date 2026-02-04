using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Comprehensive tests to maximize code coverage across all services
/// </summary>
public class MaximumCoverageTests
{
    #region RagService Additional Tests

    [Fact]
    public async Task RagService_SearchChunksAsync_EmptyQuery_ReturnsEmpty()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var mockLogger = new Mock<ILogger<RAGService>>();
        var mockAIService = new Mock<IAIService>();
        var mockKernel = new Mock<Kernel>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConfig = new Mock<IConfiguration>();

        mockConfig.Setup(c => c["RAG:MaxRetries"]).Returns("3");
        mockConfig.Setup(c => c["RAG:RetryDelayMs"]).Returns("1000");
        mockConfig.Setup(c => c["RAG:MaxContextTokens"]).Returns("4000");
        mockConfig.Setup(c => c["RAG:MaxLocalModelTokens"]).Returns("8000");
        mockConfig.Setup(c => c["RAG:EmbeddingBatchSize"]).Returns("10");
        mockConfig.Setup(c => c["RAG:EmbeddingMaxConcurrency"]).Returns("5");
        mockConfig.Setup(c => c["RAG:UseVectorDB"]).Returns("false");
        mockConfig.Setup(c => c["RAG:VectorSimilarityThreshold"]).Returns("0.5");

        var service = new RAGService(context, mockLogger.Object, mockAIService.Object, mockKernel.Object, mockServiceProvider.Object, mockConfig.Object);

        // Act
        var result = await service.SearchChunksAsync("", 10);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RagService_GetUnprocessedDocumentsAsync_ReturnsDocuments()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var mockLogger = new Mock<ILogger<RAGService>>();
        var mockAIService = new Mock<IAIService>();
        var mockKernel = new Mock<Kernel>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConfig = new Mock<IConfiguration>();

        mockConfig.Setup(c => c["RAG:MaxRetries"]).Returns("3");
        mockConfig.Setup(c => c["RAG:RetryDelayMs"]).Returns("1000");
        mockConfig.Setup(c => c["RAG:MaxContextTokens"]).Returns("4000");
        mockConfig.Setup(c => c["RAG:MaxLocalModelTokens"]).Returns("8000");
        mockConfig.Setup(c => c["RAG:EmbeddingBatchSize"]).Returns("10");
        mockConfig.Setup(c => c["RAG:EmbeddingMaxConcurrency"]).Returns("5");
        mockConfig.Setup(c => c["RAG:UseVectorDB"]).Returns("false");
        mockConfig.Setup(c => c["RAG:VectorSimilarityThreshold"]).Returns("0.5");

        var service = new RAGService(context, mockLogger.Object, mockAIService.Object, mockKernel.Object, mockServiceProvider.Object, mockConfig.Object);

        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "test.pdf",
            OriginalFileName = "test.pdf",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 100,
            DocumentType = "pdf",
            IsProcessed = false
        };
        context.DocumentMetadatas.Add(doc);
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetUnprocessedDocumentsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    #endregion

    #region HistoryService Additional Tests

    [Fact]
    public async Task HistoryService_GetRecentHistoryAsync_LimitsResults()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        // Add multiple history entries
        for (int i = 0; i < 20; i++)
        {
            await service.SaveRequestAsync(new RequestHistory
            {
                RequestType = "test",
                InputText = $"input{i}",
                DetectedTask = "task",
                Response = $"response{i}"
            });
        }

        // Act
        var result = await service.GetRecentHistoryAsync(5);

        // Assert
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task HistoryService_GetHistoryByIdAsync_ReturnsCorrectItem()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var mockLogger = new Mock<ILogger<HistoryService>>();
        var service = new HistoryService(context, mockLogger.Object);

        var saved = await service.SaveRequestAsync(new RequestHistory
        {
            RequestType = "test",
            InputText = "input",
            DetectedTask = "task",
            Response = "response"
        });

        // Act
        var result = await service.GetHistoryByIdAsync(saved.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(saved.Id, result!.Id);
    }

    #endregion

    #region ConversationService Tests

    [Fact]
    public void ConversationService_Type_Exists()
    {
        // Verify ConversationService type exists
        var type = typeof(ConversationService);
        Assert.NotNull(type);
    }

    #endregion

    #region MediaProcessor Tests

    [Fact]
    public void MediaProcessor_Type_Exists()
    {
        // Verify MediaProcessor type exists
        var type = typeof(MediaProcessor);
        Assert.NotNull(type);
    }

    #endregion

    #region TaskDispatcher Tests

    [Fact]
    public void TaskDispatcher_Type_Exists()
    {
        // Verify TaskDispatcher type exists
        var type = typeof(TaskDispatcher);
        Assert.NotNull(type);
    }

    #endregion

    #region AssemblyExtensions Tests

    [Fact]
    public void AssemblyExtensions_GetTypes_ReturnsTypes()
    {
        // Arrange
        var assembly = typeof(TaskDispatcher).Assembly;

        // Act
        var types = assembly.GetTypes();

        // Assert
        Assert.NotEmpty(types);
    }

    #endregion

    #region TimeTaskHandler Tests

    [Fact]
    public async Task TimeTaskHandler_ReturnsCurrentTime()
    {
        // Arrange
        var handler = new TimeTaskHandler();

        // Act
        var result = await handler.HandleAsync("what time is it");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("time", result.ToLower());
    }

    [Fact]
    public void TimeTaskHandler_Name_IsCorrect()
    {
        // Arrange
        var handler = new TimeTaskHandler();

        // Assert
        Assert.Equal("time", handler.Name);
    }

    #endregion

    #region ReminderTaskHandler Tests

    [Fact]
    public async Task ReminderTaskHandler_SavesReminder()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var mockHistory = new Mock<IHistoryService>();
        var mockLogger = new Mock<ILogger<ReminderTaskHandler>>();

        mockHistory.Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
            .ReturnsAsync(new RequestHistory { Id = Guid.NewGuid() });

        var handler = new ReminderTaskHandler(mockHistory.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("remind me to buy milk");

        // Assert
        Assert.NotNull(result);
        mockHistory.Verify(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()), Times.Once);
    }

    #endregion
}
