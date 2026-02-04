using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests;

public class DocumentHighlightServiceTests
{
    private readonly Mock<ILogger<DocumentHighlightService>> _mockLogger;
    private readonly Mock<IMediaStorageService> _mockStorageService;
    private readonly ApplicationDbContext _context;
    private readonly DocumentHighlightService _service;

    public DocumentHighlightServiceTests()
    {
        _mockLogger = new Mock<ILogger<DocumentHighlightService>>();
        _mockStorageService = new Mock<IMediaStorageService>();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _service = new DocumentHighlightService(_context, _mockLogger.Object, _mockStorageService.Object);
    }

    [Fact]
    public async Task HighlightDocumentAsync_DocumentNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var nonExistentDocId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.HighlightDocumentAsync(nonExistentDocId, new List<Guid>()));
    }

    [Fact]
    public async Task HighlightDocumentAsync_UnsupportedDocumentType_ShouldThrowNotSupportedException()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var doc = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.docx",
            OriginalFileName = "test.docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            StorageKey = "storage-key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000,
            DocumentType = "docx",
            IsProcessed = true
        };

        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        var stream = new MemoryStream();
        _mockStorageService
            .Setup(s => s.DownloadFileAsync("storage-key"))
            .ReturnsAsync(stream);

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.HighlightDocumentAsync(documentId, new List<Guid>()));
    }

    [Fact(Skip = "PDF creation requires BouncyCastle dependency")]
    public async Task HighlightDocumentAsync_PdfWithPositionalData_ShouldHighlightAreas()
    {
        // Test skipped - requires BouncyCastle dependency for PDF creation in tests
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HighlightDocumentAsync_ImageWithPositionalData_ShouldHighlightAreas()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var doc = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.png",
            OriginalFileName = "test.png",
            ContentType = "image/png",
            StorageKey = "storage-key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000,
            DocumentType = "image",
            IsProcessed = true,
            Chunks = new List<DocumentChunk>
            {
                new DocumentChunk
                {
                    Id = chunkId,
                    DocumentMetadataId = documentId,
                    ChunkText = "OCR text",
                    PageNumber = 1,
                    ChunkIndex = 0,
                    X = 10,
                    Y = 10,
                    Width = 50,
                    Height = 20,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        // Create a minimal valid PNG image
        var imageStream = CreateMinimalImage();
        _mockStorageService
            .Setup(s => s.DownloadFileAsync("storage-key"))
            .ReturnsAsync(imageStream);

        // Act
        var result = await _service.HighlightDocumentAsync(documentId, new List<Guid> { chunkId });

        // Assert
        Assert.NotNull(result);
        Assert.True(result.CanRead);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task HighlightTextLocationsAsync_DocumentNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var nonExistentDocId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.HighlightTextLocationsAsync(nonExistentDocId, "search text"));
    }

    [Fact]
    public async Task HighlightTextLocationsAsync_NoMatchingChunks_ShouldReturnOriginalDocument()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var doc = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            OriginalFileName = "test.pdf",
            ContentType = "application/pdf",
            StorageKey = "storage-key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000,
            DocumentType = "pdf",
            IsProcessed = true,
            Chunks = new List<DocumentChunk>
            {
                new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentMetadataId = documentId,
                    ChunkText = "This is some text",
                    PageNumber = 1,
                    ChunkIndex = 0,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        var originalStream = new MemoryStream(new byte[] { 1, 2, 3 });
        _mockStorageService
            .Setup(s => s.DownloadFileAsync("storage-key"))
            .ReturnsAsync(originalStream);

        // Act
        var result = await _service.HighlightTextLocationsAsync(documentId, "nonexistent text");

        // Assert
        Assert.NotNull(result);
        Assert.Same(originalStream, result);
    }

    private MemoryStream CreateMinimalImage()
    {
        var stream = new MemoryStream();
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(100, 100))
        {
            image.Save(stream, new PngEncoder());
        }
        stream.Position = 0;
        return stream;
    }
}
