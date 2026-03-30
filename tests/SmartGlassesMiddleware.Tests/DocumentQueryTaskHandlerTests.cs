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
    public class DocumentQueryTaskHandlerTests
    {
        private Mock<IRAGService> _mockRagService = null!;
        private Mock<ILogger<DocumentQueryTaskHandler>> _mockLogger = null!;

        [SetUp]
        public void Setup()
        {
            _mockRagService = new Mock<IRAGService>();
            _mockLogger = new Mock<ILogger<DocumentQueryTaskHandler>>();
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

        [Test]
        public void Name_ShouldBeDocumentQuery()
        {
            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(true), _mockLogger.Object);
            Assert.That(handler.Name, Is.EqualTo("document-query"));
        }

        [Test]
        public void Keywords_ShouldNotBeEmpty()
        {
            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(true), _mockLogger.Object);
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public async Task HandleAsync_RagDisabled_ReturnsUnavailabilityMessage()
        {
            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(false), _mockLogger.Object);
            var result = await handler.HandleAsync("what does the document say about diabetes?");
            Assert.That(result, Does.Contain("unavailable").Or.Contain("disabled").Or.Contain("not"));
        }

        [Test]
        public async Task HandleAsync_RagEnabled_ReturnsAnswerWithSources()
        {
            var queryResult = new QueryResult
            {
                Answer = "The document discusses hypertension treatment.",
                Sources = new List<DocumentSource>
                {
                    new DocumentSource { FileName = "clinical_guide.pdf", PageNumber = 12 }
                },
                UsedAdvancedModel = false,
                TokenCount = 150
            };

            _mockRagService
                .Setup(r => r.QueryDocumentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<List<Guid>?>(), It.IsAny<List<Guid>?>(), It.IsAny<string?>()))
                .ReturnsAsync(queryResult);

            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(true), _mockLogger.Object);
            var result = await handler.HandleAsync("tell me about hypertension");

            Assert.That(result, Does.Contain("hypertension treatment"));
            Assert.That(result, Does.Contain("clinical_guide.pdf"));
        }

        [Test]
        public async Task HandleAsync_UsedAdvancedModel_ResponseContainsAdvancedModel()
        {
            var queryResult = new QueryResult
            {
                Answer = "Advanced answer here.",
                Sources = new List<DocumentSource>(),
                UsedAdvancedModel = true,
                TokenCount = 200
            };

            _mockRagService
                .Setup(r => r.QueryDocumentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<List<Guid>?>(), It.IsAny<List<Guid>?>(), It.IsAny<string?>()))
                .ReturnsAsync(queryResult);

            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(true), _mockLogger.Object);
            var result = await handler.HandleAsync("complex question");

            Assert.That(result, Does.Contain("advanced model"));
        }

        [Test]
        public async Task HandleAsync_QueryServiceThrows_ReturnsErrorMessage()
        {
            _mockRagService
                .Setup(r => r.QueryDocumentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<List<Guid>?>(), It.IsAny<List<Guid>?>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("RAG service unavailable"));

            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(true), _mockLogger.Object);
            var result = await handler.HandleAsync("tell me about the document");

            Assert.That(result, Does.Contain("Error").Or.Contain("error"));
        }

        [Test]
        public async Task HandleAsync_RagEnabled_CallsQueryDocumentsAsync()
        {
            var queryResult = new QueryResult
            {
                Answer = "Some answer.",
                Sources = new List<DocumentSource>(),
                UsedAdvancedModel = false
            };

            _mockRagService
                .Setup(r => r.QueryDocumentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<List<Guid>?>(), It.IsAny<List<Guid>?>(), It.IsAny<string?>()))
                .ReturnsAsync(queryResult);

            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(true), _mockLogger.Object);
            await handler.HandleAsync("my query text");

            _mockRagService.Verify(r => r.QueryDocumentsAsync(
                "my query text",
                It.IsAny<int>(),
                It.IsAny<List<Guid>?>(),
                It.IsAny<List<Guid>?>(),
                It.IsAny<string?>()), Times.Once);
        }

        [Test]
        public async Task HandleAsync_RagDisabled_DoesNotCallQueryDocumentsAsync()
        {
            var handler = new DocumentQueryTaskHandler(_mockRagService.Object, BuildConfig(false), _mockLogger.Object);
            await handler.HandleAsync("any question");

            _mockRagService.Verify(r => r.QueryDocumentsAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<List<Guid>?>(),
                It.IsAny<List<Guid>?>(),
                It.IsAny<string?>()), Times.Never);
        }
    }
}
