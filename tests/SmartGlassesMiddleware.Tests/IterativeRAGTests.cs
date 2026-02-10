using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartGlassesMiddleware.Tests;

public class IterativeRAGTests
{
    [Fact]
    public void RAGPlugin_SearchDocuments_ShouldTrackSources()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<RAGPlugin>>();
        
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = chunkId,
                DocumentMetadataId = documentId,
                DocumentMetadata = new DocumentMetadata
                {
                    Id = documentId,
                    FileName = "test.txt",
                    OriginalFileName = "test.txt",
                    ContentType = "text/plain",
                    StorageKey = "key",
                    UploadedAt = DateTime.UtcNow,
                    FileSize = 100,
                    DocumentType = "txt"
                },
                ChunkText = "The capital of France is Paris.",
                PageNumber = 1,
                ChunkIndex = 0,
                CreatedAt = DateTime.UtcNow
            }
        };

        mockRagService
            .Setup(r => r.SearchChunksAsync(It.IsAny<string>(), It.IsAny<int>(), null, null))
            .ReturnsAsync(chunks);

        var plugin = new RAGPlugin(mockRagService.Object, mockLogger.Object);

        // Act
        var result = plugin.SearchDocuments("What is the capital of France?").Result;

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Paris", result);
        Assert.Contains("test.txt", result);
        Assert.Single(plugin.QueriedSources);
        Assert.Equal(chunkId, plugin.QueriedSources[0].ChunkId);
    }

    [Fact]
    public void RAGPlugin_SearchDocuments_NoResults_ShouldReturnNoDocumentsMessage()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<RAGPlugin>>();
        
        mockRagService
            .Setup(r => r.SearchChunksAsync(It.IsAny<string>(), It.IsAny<int>(), null, null))
            .ReturnsAsync(new List<DocumentChunk>());

        var plugin = new RAGPlugin(mockRagService.Object, mockLogger.Object);

        // Act
        var result = plugin.SearchDocuments("What is the capital of Mars?").Result;

        // Assert
        Assert.NotNull(result);
        Assert.Contains("No relevant documents found", result);
        Assert.Empty(plugin.QueriedSources);
    }

    [Fact]
    public void RAGPlugin_SearchDocuments_MultipleResults_ShouldTrackAllSources()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<RAGPlugin>>();
        
        var documentId = Guid.NewGuid();
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                DocumentMetadata = new DocumentMetadata
                {
                    Id = documentId,
                    FileName = "test1.txt",
                    OriginalFileName = "test1.txt",
                    ContentType = "text/plain",
                    StorageKey = "key1",
                    UploadedAt = DateTime.UtcNow,
                    FileSize = 100,
                    DocumentType = "txt"
                },
                ChunkText = "First chunk of information.",
                PageNumber = 1,
                ChunkIndex = 0,
                CreatedAt = DateTime.UtcNow
            },
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                DocumentMetadata = new DocumentMetadata
                {
                    Id = documentId,
                    FileName = "test1.txt",
                    OriginalFileName = "test1.txt",
                    ContentType = "text/plain",
                    StorageKey = "key1",
                    UploadedAt = DateTime.UtcNow,
                    FileSize = 100,
                    DocumentType = "txt"
                },
                ChunkText = "Second chunk of information.",
                PageNumber = 2,
                ChunkIndex = 1,
                CreatedAt = DateTime.UtcNow
            }
        };

        mockRagService
            .Setup(r => r.SearchChunksAsync(It.IsAny<string>(), It.IsAny<int>(), null, null))
            .ReturnsAsync(chunks);

        var plugin = new RAGPlugin(mockRagService.Object, mockLogger.Object);

        // Act
        var result = plugin.SearchDocuments("test query").Result;

        // Assert
        Assert.NotNull(result);
        Assert.Contains("First chunk", result);
        Assert.Contains("Second chunk", result);
        Assert.Equal(2, plugin.QueriedSources.Count);
    }

    [Fact]
    public async Task QueryDocumentsAsync_WithIterativeRetrievalDisabled_UsesTraditionalMode()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<RAGService>>();
        var mockAIService = new Mock<IAIService>();
        var kernel = new Kernel();
        var mockConfiguration = new Mock<IConfiguration>();
        var mockTaskDispatcher = new Mock<ITaskDispatcher>();

        // Setup configuration - iterative retrieval disabled
        SetupConfiguration(mockConfiguration, useIterativeRetrieval: false);

        var services = new ServiceCollection();
        services.AddSingleton(mockTaskDispatcher.Object);
        services.AddSingleton<ILoggerFactory>(new LoggerFactory());
        var serviceProvider = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);

        // Add test data
        var documentId = Guid.NewGuid();
        var doc = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.txt",
            OriginalFileName = "test.txt",
            ContentType = "text/plain",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 100,
            DocumentType = "txt",
            IsProcessed = true
        };
        context.DocumentMetadatas.Add(doc);
        
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = documentId,
            DocumentMetadata = doc,
            ChunkText = "Test content about RAG systems.",
            PageNumber = 1,
            ChunkIndex = 0,
            CreatedAt = DateTime.UtcNow
        };
        context.DocumentChunks.Add(chunk);
        await context.SaveChangesAsync();

        mockAIService
            .Setup(a => a.ProcessRequestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test", "This is a test response about RAG systems.", false));

        var service = new RAGService(
            context,
            mockLogger.Object,
            mockAIService.Object,
            kernel,
            serviceProvider,
            mockConfiguration.Object
        );

        // Act
        var result = await service.QueryDocumentsAsync("What is a RAG system?");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Answer);
        Assert.NotEqual("local-iterative", result.ModelUsed); // Should not use iterative mode
    }

    private void SetupConfiguration(Mock<IConfiguration> config, bool useIterativeRetrieval = false)
    {
        config.Setup(c => c.GetSection("RAG:MaxRetries").Value).Returns("3");
        config.Setup(c => c.GetSection("RAG:RetryDelayMs").Value).Returns("1000");
        config.Setup(c => c.GetSection("RAG:MaxContextTokens").Value).Returns("4000");
        config.Setup(c => c.GetSection("RAG:MaxLocalModelTokens").Value).Returns("2000");
        config.Setup(c => c.GetSection("RAG:ApproximateTokensPerChar").Value).Returns("0.25");
        config.Setup(c => c.GetSection("RAG:EmbeddingBatchSize").Value).Returns("10");
        config.Setup(c => c.GetSection("RAG:EmbeddingMaxConcurrency").Value).Returns("5");
        config.Setup(c => c.GetSection("RAG:UseVectorDB").Value).Returns("false");
        config.Setup(c => c.GetSection("RAG:VectorSimilarityThreshold").Value).Returns("0.7");
        config.Setup(c => c.GetSection("RAG:QueryTimeoutSeconds").Value).Returns("120");
        config.Setup(c => c.GetSection("RAG:ConversationMessageLimit").Value).Returns("5");
        config.Setup(c => c.GetSection("RAG:ConversationHistoryPercentage").Value).Returns("25");
        config.Setup(c => c.GetSection("RAG:UseIterativeRetrieval").Value).Returns(useIterativeRetrieval.ToString().ToLower());
        config.Setup(c => c.GetSection("RAG:MaxIterativeQueries").Value).Returns("5");
    }
}
