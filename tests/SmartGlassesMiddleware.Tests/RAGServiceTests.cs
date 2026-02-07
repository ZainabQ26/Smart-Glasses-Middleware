using System;
using System.Collections.Generic;
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

public class RAGServiceTests
{
    private readonly Mock<ILogger<RAGService>> _mockLogger;
    private readonly Mock<IAIService> _mockAIService;
    private readonly Kernel _kernel;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ITaskDispatcher> _mockTaskDispatcher;
    private readonly IServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _context;
    private readonly RAGService _service;

    public RAGServiceTests()
    {
        _mockLogger = new Mock<ILogger<RAGService>>();
        _mockAIService = new Mock<IAIService>();
        _kernel = new Kernel();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockTaskDispatcher = new Mock<ITaskDispatcher>();

        // Setup configuration defaults using GetValue<T> method
        _mockConfiguration.Setup(c => c.GetSection("RAG:MaxRetries").Value).Returns("3");
        _mockConfiguration.Setup(c => c.GetSection("RAG:RetryDelayMs").Value).Returns("1000");
        _mockConfiguration.Setup(c => c.GetSection("RAG:MaxContextTokens").Value).Returns("4000");
        _mockConfiguration.Setup(c => c.GetSection("RAG:MaxLocalModelTokens").Value).Returns("2000");
        _mockConfiguration.Setup(c => c.GetSection("RAG:ApproximateTokensPerChar").Value).Returns("0.25");
        _mockConfiguration.Setup(c => c.GetSection("RAG:EmbeddingBatchSize").Value).Returns("10");
        _mockConfiguration.Setup(c => c.GetSection("RAG:EmbeddingMaxConcurrency").Value).Returns("5");
        _mockConfiguration.Setup(c => c.GetSection("RAG:UseVectorDB").Value).Returns("true");
        _mockConfiguration.Setup(c => c.GetSection("RAG:VectorSimilarityThreshold").Value).Returns("0.7");
        _mockConfiguration.Setup(c => c.GetSection("RAG:QueryTimeoutSeconds").Value).Returns("120");

        var services = new ServiceCollection();
        services.AddSingleton(_mockTaskDispatcher.Object);
        _serviceProvider = services.BuildServiceProvider();

        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _service = new RAGService(
            _context,
            _mockLogger.Object,
            _mockAIService.Object,
            _kernel,
            _serviceProvider,
            _mockConfiguration.Object
        );
    }

    [Fact]
    public async Task SearchChunksAsync_WithKeywordMatch_ShouldReturnRelevantChunks()
    {
        // Arrange
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

        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                DocumentMetadata = doc,
                ChunkText = "The weather is sunny today in California.",
                PageNumber = 1,
                ChunkIndex = 0,
                CreatedAt = DateTime.UtcNow
            },
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                DocumentMetadata = doc,
                ChunkText = "The stock market is performing well.",
                PageNumber = 1,
                ChunkIndex = 1,
                CreatedAt = DateTime.UtcNow
            },
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                DocumentMetadata = doc,
                ChunkText = "Weather patterns are changing due to climate.",
                PageNumber = 1,
                ChunkIndex = 2,
                CreatedAt = DateTime.UtcNow
            }
        };

        _context.DocumentMetadatas.Add(doc);
        _context.DocumentChunks.AddRange(chunks);
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.SearchChunksAsync("weather", 2);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, chunk => Assert.Contains("weather", chunk.ChunkText.ToLower()));
    }

    [Fact]
    public async Task QueryDocumentsAsync_NoRelevantChunks_ShouldReturnNoResultsMessage()
    {
        // Arrange
        var query = "nonexistent topic";

        // Act
        var result = await _service.QueryDocumentsAsync(query);

        // Assert
        Assert.Contains("No relevant information found", result.Answer);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldUpdateChunksWithEmbeddings()
    {
        // Arrange
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

        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = documentId,
            ChunkText = "Test chunk for embedding",
            PageNumber = 1,
            ChunkIndex = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentMetadatas.Add(doc);
        _context.DocumentChunks.Add(chunk);
        await _context.SaveChangesAsync();

        // Act
        await _service.GenerateEmbeddingsAsync(documentId);

        // Assert
        var updatedChunk = await _context.DocumentChunks.FindAsync(chunk.Id);
        Assert.NotNull(updatedChunk?.Embedding);
    }
}
