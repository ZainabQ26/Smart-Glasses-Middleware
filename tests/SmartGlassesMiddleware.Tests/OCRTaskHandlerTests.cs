using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class OCRTaskHandlerTests
    {
        private Mock<ILogger<OCRTaskHandler>> _mockLogger = null!;
        private Mock<IDocumentProcessingService> _mockProcessingService = null!;
        private Mock<IRAGService> _mockRagService = null!;
        private Guid _testDocumentId;
        private DocumentMetadata _testMetadata = null!;

        [SetUp]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<OCRTaskHandler>>();
            _mockProcessingService = new Mock<IDocumentProcessingService>();
            _mockRagService = new Mock<IRAGService>();
            _testDocumentId = Guid.NewGuid();

            _testMetadata = new DocumentMetadata
            {
                Id = _testDocumentId,
                FileName = "test.pdf",
                Chunks = new List<DocumentChunk> { new DocumentChunk(), new DocumentChunk() },
                ProcessedAt = DateTime.UtcNow
            };

            _mockProcessingService
                .Setup(s => s.ReprocessDocumentAsync(_testDocumentId))
                .ReturnsAsync(_testMetadata);

            _mockRagService
                .Setup(s => s.GenerateEmbeddingsAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);
        }

        private static IConfiguration BuildConfig(bool ragEnabled)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RAG:Enabled"] = ragEnabled ? "true" : "false"
                })
                .Build();
        }

        // ── OCRTaskHandler ────────────────────────────────────────────────────

        [Test]
        public void OCRHandler_Name_ShouldBeOcr()
        {
            var handler = new OCRTaskHandler(_mockLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            Assert.That(handler.Name, Is.EqualTo("ocr"));
        }

        [Test]
        public async Task OCRHandler_NotAGuid_ReturnsGeneralOcrMessage()
        {
            var handler = new OCRTaskHandler(_mockLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            var result = await handler.HandleAsync("scan the document");
            Assert.That(result, Does.Contain("Tesseract").Or.Contain("OCR"));
        }

        [Test]
        public async Task OCRHandler_ValidGuid_CallsReprocessAndReturnsSuccessMessage()
        {
            var handler = new OCRTaskHandler(_mockLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            var result = await handler.HandleAsync(_testDocumentId.ToString());

            _mockProcessingService.Verify(s => s.ReprocessDocumentAsync(_testDocumentId), Times.Once);
            Assert.That(result, Does.Contain(_testDocumentId.ToString()));
        }

        [Test]
        public async Task OCRHandler_RagEnabled_ValidGuid_CallsGenerateEmbeddings()
        {
            var handler = new OCRTaskHandler(_mockLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            await handler.HandleAsync(_testDocumentId.ToString());

            _mockRagService.Verify(s => s.GenerateEmbeddingsAsync(_testDocumentId), Times.Once);
        }

        [Test]
        public async Task OCRHandler_RagDisabled_ValidGuid_DoesNotCallGenerateEmbeddings()
        {
            var handler = new OCRTaskHandler(_mockLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(false));
            await handler.HandleAsync(_testDocumentId.ToString());

            _mockRagService.Verify(s => s.GenerateEmbeddingsAsync(It.IsAny<Guid>()), Times.Never);
        }

        // ── OCRRetryTaskHandler ───────────────────────────────────────────────

        [Test]
        public void OCRRetryHandler_Name_ShouldBeOcrRetry()
        {
            var mockRetryLogger = new Mock<ILogger<OCRRetryTaskHandler>>();
            var handler = new OCRRetryTaskHandler(mockRetryLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            Assert.That(handler.Name, Is.EqualTo("ocr-retry"));
        }

        [Test]
        public async Task OCRRetryHandler_NotAGuid_ReturnsExpectsDocumentIdMessage()
        {
            var mockRetryLogger = new Mock<ILogger<OCRRetryTaskHandler>>();
            var handler = new OCRRetryTaskHandler(mockRetryLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            var result = await handler.HandleAsync("retry ocr");
            Assert.That(result, Does.Contain("document ID").Or.Contain("expects a document"));
        }

        [Test]
        public async Task OCRRetryHandler_ValidGuid_CallsReprocessAndReturnsSuccessMessage()
        {
            var mockRetryLogger = new Mock<ILogger<OCRRetryTaskHandler>>();
            var handler = new OCRRetryTaskHandler(mockRetryLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            var result = await handler.HandleAsync(_testDocumentId.ToString());

            _mockProcessingService.Verify(s => s.ReprocessDocumentAsync(_testDocumentId), Times.Once);
            Assert.That(result, Does.Contain(_testDocumentId.ToString()));
        }

        [Test]
        public async Task OCRRetryHandler_ServiceThrows_ReturnsOcrRetryErrorMessage()
        {
            _mockProcessingService
                .Setup(s => s.ReprocessDocumentAsync(_testDocumentId))
                .ThrowsAsync(new Exception("Processing failed"));

            var mockRetryLogger = new Mock<ILogger<OCRRetryTaskHandler>>();
            var handler = new OCRRetryTaskHandler(mockRetryLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            var result = await handler.HandleAsync(_testDocumentId.ToString());

            Assert.That(result, Does.StartWith("OCR retry error:"));
        }

        [Test]
        public async Task OCRRetryHandler_RagEnabled_ValidGuid_CallsGenerateEmbeddings()
        {
            var mockRetryLogger = new Mock<ILogger<OCRRetryTaskHandler>>();
            var handler = new OCRRetryTaskHandler(mockRetryLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(true));
            await handler.HandleAsync(_testDocumentId.ToString());

            _mockRagService.Verify(s => s.GenerateEmbeddingsAsync(_testDocumentId), Times.Once);
        }

        [Test]
        public async Task OCRRetryHandler_RagDisabled_ValidGuid_DoesNotCallGenerateEmbeddings()
        {
            var mockRetryLogger = new Mock<ILogger<OCRRetryTaskHandler>>();
            var handler = new OCRRetryTaskHandler(mockRetryLogger.Object, _mockProcessingService.Object, _mockRagService.Object, BuildConfig(false));
            await handler.HandleAsync(_testDocumentId.ToString());

            _mockRagService.Verify(s => s.GenerateEmbeddingsAsync(It.IsAny<Guid>()), Times.Never);
        }
    }
}
