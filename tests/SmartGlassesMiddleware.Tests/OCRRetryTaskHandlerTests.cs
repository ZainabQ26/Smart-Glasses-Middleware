using System;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests;

public class OCRRetryTaskHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidDocumentId_CallsReprocessAndGenerateEmbeddings()
    {
        // Arrange
        var docId = Guid.NewGuid();

        var mockLogger = new Mock<ILogger<OCRRetryTaskHandler>>();
        var mockProcessing = new Mock<IDocumentProcessingService>();
        var mockRag = new Mock<IRAGService>();

        var metadata = new DocumentMetadata
        {
            Id = docId,
            FileName = "test.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = true,
            ProcessedAt = DateTime.UtcNow
        };
        metadata.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), ChunkText = "a", CreatedAt = DateTime.UtcNow });

        mockProcessing.Setup(s => s.ReprocessDocumentAsync(docId)).ReturnsAsync(metadata);
        mockRag.Setup(r => r.GenerateEmbeddingsAsync(docId)).Returns(Task.CompletedTask).Verifiable();

        var handler = new OCRRetryTaskHandler(mockLogger.Object, mockProcessing.Object, mockRag.Object);

        // Act
        var result = await handler.HandleAsync(docId.ToString());

        // Assert
        Assert.Contains("Manual OCR retry complete", result);
        mockRag.Verify(r => r.GenerateEmbeddingsAsync(docId), Times.Once);
    }
}
