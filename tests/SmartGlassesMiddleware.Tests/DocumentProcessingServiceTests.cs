using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Tests;

public class DocumentProcessingServiceTests
{
    private readonly Mock<ILogger<DocumentProcessingService>> _mockLogger;
    private readonly Mock<IMediaStorageService> _mockStorageService;
    private readonly ApplicationDbContext _context;
    private readonly DocumentProcessingService _service;

    public DocumentProcessingServiceTests()
    {
        _mockLogger = new Mock<ILogger<DocumentProcessingService>>();
        _mockStorageService = new Mock<IMediaStorageService>();
        
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        
        _service = new DocumentProcessingService(
            _mockLogger.Object,
            _mockStorageService.Object,
            _context
        );
    }

    [Fact]
    public async Task ProcessDocumentAsync_TextFile_ShouldCreateChunks()
    {
        // Arrange
        var content = "This is a test document. It has multiple sentences. This will be chunked appropriately.";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var fileName = "test.txt";
        var contentType = "text/plain";

        _mockStorageService
            .Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("test-storage-key");

        // Act
        var result = await _service.ProcessDocumentAsync(stream, fileName, contentType);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(fileName, result.FileName);
        Assert.Equal("txt", result.DocumentType);
        Assert.True(result.IsProcessed);
        Assert.NotEmpty(result.Chunks);
        
        // Verify storage was called
        _mockStorageService.Verify(
            s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Once
        );
    }

    [Fact]
    public async Task ProcessDocumentAsync_InvalidDocumentType_ShouldHandleGracefully()
    {
        // Arrange
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        var fileName = "test.unknown";
        var contentType = "application/unknown";

        _mockStorageService
            .Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("test-storage-key");

        // Act
        var result = await _service.ProcessDocumentAsync(stream, fileName, contentType);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsProcessed);
        Assert.False(string.IsNullOrWhiteSpace(result.ProcessingError));
    }

    [Fact]
    public async Task ExtractChunksAsync_ShouldReturnChunksInOrder()
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
                ChunkText = "Third chunk",
                PageNumber = 1,
                ChunkIndex = 2,
                CreatedAt = DateTime.UtcNow
            },
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                ChunkText = "First chunk",
                PageNumber = 1,
                ChunkIndex = 0,
                CreatedAt = DateTime.UtcNow
            },
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentMetadataId = documentId,
                ChunkText = "Second chunk",
                PageNumber = 1,
                ChunkIndex = 1,
                CreatedAt = DateTime.UtcNow
            }
        };

        _context.DocumentMetadatas.Add(doc);
        _context.DocumentChunks.AddRange(chunks);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ExtractChunksAsync(documentId);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("First chunk", result[0].ChunkText);
        Assert.Equal("Second chunk", result[1].ChunkText);
        Assert.Equal("Third chunk", result[2].ChunkText);
    }
}
