using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

namespace SmartGlassesMiddleware.Tests;

public class DocumentLocationTests
{
    private readonly Mock<ILogger<DocumentsController>> _mockLogger;
    private readonly Mock<IDocumentProcessingService> _mockProcessingService;
    private readonly Mock<IRAGService> _mockRagService;
    private readonly Mock<IDocumentHighlightService> _mockHighlightService;
    private readonly ApplicationDbContext _context;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IRequestMonitoringService> _mockMonitoringService;
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly Mock<IMediaStorageService> _mockMediaStorageService;
    private readonly Mock<IConversationService> _mockConversationService;
    private readonly Mock<ICacheService> _mockCacheService;
    private readonly DocumentsController _controller;

    public DocumentLocationTests()
    {
        _mockLogger = new Mock<ILogger<DocumentsController>>();
        _mockProcessingService = new Mock<IDocumentProcessingService>();
        _mockRagService = new Mock<IRAGService>();
        _mockHighlightService = new Mock<IDocumentHighlightService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockMonitoringService = new Mock<IRequestMonitoringService>();
        _mockHistoryService = new Mock<IHistoryService>();
        _mockMediaStorageService = new Mock<IMediaStorageService>();
        _mockConversationService = new Mock<IConversationService>();
        _mockCacheService = new Mock<ICacheService>();

        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _controller = new DocumentsController(
            _mockLogger.Object,
            _mockProcessingService.Object,
            _mockRagService.Object,
            _mockHighlightService.Object,
            _context,
            _mockConfiguration.Object,
            _mockMonitoringService.Object,
            _mockHistoryService.Object,
            _mockMediaStorageService.Object,
            _mockConversationService.Object,
            null,
            _mockCacheService.Object,
            null
        );
    }

    [Fact]
    public async Task FindTextLocation_WithValidSearch_ReturnsLocations()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            OriginalFileName = "test.txt",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 100,
            DocumentType = "pdf",
            IsProcessed = true,
            Chunks = new List<DocumentChunk>
            {
                new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentMetadataId = documentId,
                    ChunkText = "This is a test document with location data.",
                    PageNumber = 1,
                    ChunkIndex = 0,
                    X = 100,
                    Y = 200,
                    Width = 400,
                    Height = 20,
                    CreatedAt = DateTime.UtcNow
                },
                new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentMetadataId = documentId,
                    ChunkText = "Another test chunk on page 2.",
                    PageNumber = 2,
                    ChunkIndex = 1,
                    X = 450,
                    Y = 150,
                    Width = 100,
                    Height = 15,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        _context.DocumentMetadatas.Add(document);
        await _context.SaveChangesAsync();

        var request = new FindLocationRequest { SearchText = "test" };

        // Act
        var result = await _controller.FindTextLocation(documentId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FindLocationResponse>(okResult.Value);
        Assert.Equal(2, response.MatchCount);
        Assert.Equal(2, response.Locations.Count);
        
        // Verify first location
        var firstLocation = response.Locations[0];
        Assert.Equal(1, firstLocation.PageNumber);
        Assert.Equal(100, firstLocation.X);
        Assert.Equal(200, firstLocation.Y);
        Assert.NotNull(firstLocation.Position);
        Assert.Contains("test", firstLocation.ContextText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindTextLocation_NoMatches_ReturnsEmptyList()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            OriginalFileName = "test.txt",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 100,
            DocumentType = "pdf",
            IsProcessed = true,
            Chunks = new List<DocumentChunk>
            {
                new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentMetadataId = documentId,
                    ChunkText = "This chunk has no matching text.",
                    PageNumber = 1,
                    ChunkIndex = 0,
                    X = 100,
                    Y = 200,
                    Width = 400,
                    Height = 20,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        _context.DocumentMetadatas.Add(document);
        await _context.SaveChangesAsync();

        var request = new FindLocationRequest { SearchText = "nonexistent" };

        // Act
        var result = await _controller.FindTextLocation(documentId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FindLocationResponse>(okResult.Value);
        Assert.Equal(0, response.MatchCount);
        Assert.Empty(response.Locations);
    }

    [Fact]
    public async Task FindTextLocation_DocumentNotFound_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var request = new FindLocationRequest { SearchText = "test" };

        // Act
        var result = await _controller.FindTextLocation(nonExistentId, request);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task FindTextLocation_EmptySearchText_ReturnsBadRequest()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = new FindLocationRequest { SearchText = "" };

        // Act
        var result = await _controller.FindTextLocation(documentId, request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void PdfConstants_HasCorrectValues()
    {
        // Assert - US Letter dimensions
        Assert.Equal(612, PdfConstants.PageWidth);
        Assert.Equal(792, PdfConstants.PageHeight);
    }

    [Fact]
    public async Task QueryResults_IncludePositionData()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        
        var chunk = new DocumentChunk
        {
            Id = chunkId,
            DocumentMetadataId = documentId,
            ChunkText = "Test chunk with position",
            PageNumber = 1,
            ChunkIndex = 0,
            X = 100,
            Y = 200,
            Width = 300,
            Height = 15,
            CreatedAt = DateTime.UtcNow,
            DocumentMetadata = new DocumentMetadata
            {
                Id = documentId,
                FileName = "test.pdf",
                ContentType = "application/pdf",
                StorageKey = "key",
                UploadedAt = DateTime.UtcNow,
                FileSize = 100,
                DocumentType = "pdf"
            }
        };

        // Create DocumentSource as would be done in RAGService
        var source = new DocumentSource
        {
            DocumentId = chunk.DocumentMetadataId,
            FileName = chunk.DocumentMetadata.FileName,
            PageNumber = chunk.PageNumber,
            ChunkId = chunk.Id,
            ChunkText = chunk.ChunkText,
            X = chunk.X,
            Y = chunk.Y,
            Width = chunk.Width,
            Height = chunk.Height
        };

        // Assert
        Assert.Equal(100, source.X);
        Assert.Equal(200, source.Y);
        Assert.Equal(300, source.Width);
        Assert.Equal(15, source.Height);
        Assert.Equal(1, source.PageNumber);
    }

    [Fact]
    public void TextLocation_ContainsAllRequiredFields()
    {
        // Arrange & Act
        var location = new TextLocation
        {
            DocumentId = Guid.NewGuid(),
            DocumentName = "test.pdf",
            ChunkId = Guid.NewGuid(),
            PageNumber = 2,
            X = 450.5,
            Y = 120.3,
            Width = 80.0,
            Height = 12.0,
            Position = "bottom right",
            ContextText = "...test context..."
        };

        // Assert
        Assert.NotEqual(Guid.Empty, location.DocumentId);
        Assert.Equal("test.pdf", location.DocumentName);
        Assert.Equal(2, location.PageNumber);
        Assert.Equal(450.5, location.X);
        Assert.Equal(120.3, location.Y);
        Assert.Equal("bottom right", location.Position);
        Assert.NotNull(location.ContextText);
    }
}
