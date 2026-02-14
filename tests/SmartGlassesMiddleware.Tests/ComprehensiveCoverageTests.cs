using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

public class DocumentProcessingServiceEnhancedTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<DocumentProcessingService>> _mockLogger;
    private readonly Mock<IMediaStorageService> _mockStorageService;
    private readonly DocumentProcessingService _service;

    public DocumentProcessingServiceEnhancedTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _mockLogger = new Mock<ILogger<DocumentProcessingService>>();
        _mockStorageService = new Mock<IMediaStorageService>();
        var mockMonitoringService = new Mock<IRequestMonitoringService>();
        var mockConfiguration = new Mock<IConfiguration>();

        _service = new DocumentProcessingService(
            _mockLogger.Object, 
            _mockStorageService.Object, 
            _context,
            mockMonitoringService.Object,
            mockConfiguration.Object);
    }

    [Fact]
    public async Task ExtractChunksAsync_WithExistingDocument_ShouldReturnChunks()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var doc = new DocumentMetadata
        {
            Id = docId,
            FileName = "test.txt",
            OriginalFileName = "test.txt",
            ContentType = "text/plain",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 100,
            DocumentType = "txt",
            IsProcessed = true
        };

        doc.Chunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = docId,
            ChunkText = "Chunk 1",
            PageNumber = 1,
            ChunkIndex = 0
        });

        doc.Chunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = docId,
            ChunkText = "Chunk 2",
            PageNumber = 1,
            ChunkIndex = 1
        });

        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        // Act
        var chunks = await _service.ExtractChunksAsync(docId);

        // Assert
        Assert.NotNull(chunks);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
    }

    [Fact]
    public async Task ReprocessDocumentAsync_WithNonExistentDocument_ShouldThrow()
    {
        // Arrange
        var docId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ReprocessDocumentAsync(docId));
    }
}

public class AssemblyLoadTests
{
    [Fact]
    public void SmartGlassesMiddleware_Assembly_ShouldLoad()
    {
        // This test verifies the main assembly loads correctly
        var assembly = typeof(SmartGlassesMiddleware.Controllers.TasksController).Assembly;
        Assert.NotNull(assembly);
        Assert.Contains("SmartGlassesMiddleware", assembly.FullName);
    }

    [Fact]
    public void Controllers_Assembly_HasTypes()
    {
        // Verify controllers exist
        var controllerTypes = new[]
        {
            typeof(SmartGlassesMiddleware.Controllers.TasksController),
            typeof(SmartGlassesMiddleware.Controllers.HistoryController),
            typeof(SmartGlassesMiddleware.Controllers.ConversationsController),
            typeof(SmartGlassesMiddleware.Controllers.DocumentsController),
        };

        foreach (var type in controllerTypes)
        {
            Assert.NotNull(type);
        }
    }
}
