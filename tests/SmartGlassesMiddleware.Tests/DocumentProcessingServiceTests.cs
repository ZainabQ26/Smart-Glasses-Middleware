using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Tests;

public class DocumentProcessingServiceTests
{
    private readonly Mock<ILogger<DocumentProcessingService>> _mockLogger;
    private readonly Mock<IMediaStorageService> _mockStorageService;
    private readonly Mock<IRequestMonitoringService> _mockMonitoringService;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly ApplicationDbContext _context;
    private readonly DocumentProcessingService _service;

    public DocumentProcessingServiceTests()
    {
        _mockLogger = new Mock<ILogger<DocumentProcessingService>>();
        _mockStorageService = new Mock<IMediaStorageService>();
        _mockMonitoringService = new Mock<IRequestMonitoringService>();
        _mockConfiguration = new Mock<IConfiguration>();

        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _service = new DocumentProcessingService(
            _mockLogger.Object,
            _mockStorageService.Object,
            _context,
            _mockMonitoringService.Object,
            _mockConfiguration.Object
        );
    }

    [Fact]
    public async Task ProcessDocumentAsync_TextFile_ShouldCreateMarkdownAndChunks()
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
        Assert.EndsWith(".md", result.FileName); // Now produces markdown
        Assert.Equal("markdown", result.DocumentType); // Document type is now markdown
        Assert.Equal("text/markdown", result.ContentType); // Content type is markdown
        Assert.True(result.IsProcessed);
        Assert.NotEmpty(result.Chunks);

        // Verify storage was called with markdown content type
        _mockStorageService.Verify(
            s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), "text/markdown"),
            Times.Once
        );
    }

    [Fact]
    public async Task ProcessDocumentAsync_XMLFile_ShouldConvertToMarkdown()
    {
        // Arrange
        var xmlContent = "<?xml version=\"1.0\"?><root><item>Test</item></root>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlContent));
        var fileName = "test.xml";
        var contentType = "application/xml";

        _mockStorageService
            .Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("test-storage-key");

        // Act
        var result = await _service.ProcessDocumentAsync(stream, fileName, contentType);

        // Assert
        Assert.NotNull(result);
        Assert.EndsWith(".md", result.FileName);
        Assert.Equal("markdown", result.DocumentType);
        Assert.True(result.IsProcessed);
        Assert.NotEmpty(result.Chunks);
    }

    [Fact]
    public async Task ProcessDocumentAsync_WithRequestId_ShouldReportProgress()
    {
        // Arrange
        var content = "Test content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var fileName = "test.txt";
        var contentType = "text/plain";
        var requestId = "test-request-123";

        _mockStorageService
            .Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("test-storage-key");

        // Act
        var result = await _service.ProcessDocumentAsync(stream, fileName, contentType, requestId);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsProcessed);

        // Verify progress was reported
        _mockMonitoringService.Verify(
            m => m.UpdateRequestStatus(
                requestId, 
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<int>()),
            Times.AtLeastOnce
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

        // Act & Assert - Should throw NotSupportedException
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await _service.ProcessDocumentAsync(stream, fileName, contentType)
        );
    }

    [Fact]
    public async Task ExtractChunksAsync_ShouldReturnChunksInOrder()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var doc = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.md",
            OriginalFileName = "test.txt",
            ContentType = "text/markdown",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 100,
            DocumentType = "markdown",
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
