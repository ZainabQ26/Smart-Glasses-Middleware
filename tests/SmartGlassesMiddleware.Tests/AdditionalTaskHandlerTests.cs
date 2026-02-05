using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

public class AdditionalTaskHandlerTests
{
    #region AdvancedTaskHandler Tests

    [Fact]
    public void AdvancedTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<AdvancedTaskHandler>>();
        var handler = new AdvancedTaskHandler(mockHttpFactory.Object, mockConfig.Object, mockLogger.Object);

        // Assert
        Assert.Equal("advanced", handler.Name);
    }

    [Fact]
    public async Task AdvancedTaskHandler_Returns_Error_When_Endpoint_Not_Configured()
    {
        // Arrange
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["AI:AdvancedModelEndpoint"]).Returns((string?)null);
        var mockLogger = new Mock<ILogger<AdvancedTaskHandler>>();
        var handler = new AdvancedTaskHandler(mockHttpFactory.Object, mockConfig.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("test input");

        // Assert
        Assert.Contains("not configured", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdvancedTaskHandler_Calls_Endpoint_Successfully()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { response = "Advanced model response" }))
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["AI:AdvancedModelEndpoint"]).Returns("http://localhost:5000/api/advanced");
        mockConfig.Setup(c => c["AI:AdvancedModelName"]).Returns("gpt-4");

        var mockLogger = new Mock<ILogger<AdvancedTaskHandler>>();
        var handler = new AdvancedTaskHandler(mockHttpFactory.Object, mockConfig.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("test input");

        // Assert
        Assert.Equal("Advanced model response", result);
    }

    [Fact]
    public async Task AdvancedTaskHandler_Handles_Non_Success_Response()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                ReasonPhrase = "Internal Server Error"
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["AI:AdvancedModelEndpoint"]).Returns("http://localhost:5000/api/advanced");

        var mockLogger = new Mock<ILogger<AdvancedTaskHandler>>();
        var handler = new AdvancedTaskHandler(mockHttpFactory.Object, mockConfig.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("test input");

        // Assert
        Assert.Contains("500", result);
        Assert.Contains("Internal Server Error", result);
    }

    [Fact]
    public async Task AdvancedTaskHandler_Handles_Exception()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["AI:AdvancedModelEndpoint"]).Returns("http://localhost:5000/api/advanced");

        var mockLogger = new Mock<ILogger<AdvancedTaskHandler>>();
        var handler = new AdvancedTaskHandler(mockHttpFactory.Object, mockConfig.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("test input");

        // Assert
        Assert.Contains("Error calling advanced model", result, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region DocumentQueryTaskHandler Tests

    [Fact]
    public void DocumentQueryTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        var mockLogger = new Mock<ILogger<DocumentQueryTaskHandler>>();
        var handler = new DocumentQueryTaskHandler(mockRagService.Object, mockLogger.Object);

        // Assert
        Assert.Equal("document-query", handler.Name);
    }

    [Fact]
    public async Task DocumentQueryTaskHandler_Returns_Query_Results()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        mockRagService.Setup(r => r.QueryDocumentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<List<Guid>?>()))
            .ReturnsAsync(new QueryResult
            {
                Answer = "Test answer",
                Sources = new List<DocumentSource>
                {
                    new DocumentSource { FileName = "test.pdf", PageNumber = 1 }
                },
                UsedAdvancedModel = false,
                TokenCount = 100
            });

        var mockLogger = new Mock<ILogger<DocumentQueryTaskHandler>>();
        var handler = new DocumentQueryTaskHandler(mockRagService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("What is the test?");

        // Assert
        Assert.Contains("Test answer", result);
        Assert.Contains("test.pdf", result);
        Assert.Contains("Ollama", result);
    }

    [Fact]
    public async Task DocumentQueryTaskHandler_Handles_Error()
    {
        // Arrange
        var mockRagService = new Mock<IRAGService>();
        mockRagService.Setup(r => r.QueryDocumentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<List<Guid>?>()))
            .ThrowsAsync(new Exception("Database error"));

        var mockLogger = new Mock<ILogger<DocumentQueryTaskHandler>>();
        var handler = new DocumentQueryTaskHandler(mockRagService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("What is the test?");

        // Assert
        Assert.Contains("Error querying documents", result);
        Assert.Contains("Database error", result);
    }

    #endregion

    #region DocumentSearchTaskHandler Tests

    [Fact]
    public void DocumentSearchTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_DocSearch")
            .Options;
        var context = new ApplicationDbContext(options);
        var mockLogger = new Mock<ILogger<DocumentSearchTaskHandler>>();
        var handler = new DocumentSearchTaskHandler(context, mockLogger.Object);

        // Assert
        Assert.Equal("document-search", handler.Name);
    }

    [Fact]
    public async Task DocumentSearchTaskHandler_Finds_Matching_Text()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_DocSearchFind_" + Guid.NewGuid())
            .Options;
        using var context = new ApplicationDbContext(options);

        var docId = Guid.NewGuid();
        var doc = new DocumentMetadata
        {
            Id = docId,
            FileName = "test.pdf",
            UploadedAt = DateTime.UtcNow
        };
        doc.Chunks.Add(new DocumentChunk
        {
            ChunkText = "This is a test document with important information",
            PageNumber = 1,
            ChunkIndex = 0
        });
        context.DocumentMetadatas.Add(doc);
        await context.SaveChangesAsync();

        var mockLogger = new Mock<ILogger<DocumentSearchTaskHandler>>();
        var handler = new DocumentSearchTaskHandler(context, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("important");

        // Assert
        Assert.Contains("important", result);
        Assert.Contains("test.pdf", result);
    }

    [Fact]
    public async Task DocumentSearchTaskHandler_Returns_No_Matches()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_DocSearchNoMatch_" + Guid.NewGuid())
            .Options;
        using var context = new ApplicationDbContext(options);

        var mockLogger = new Mock<ILogger<DocumentSearchTaskHandler>>();
        var handler = new DocumentSearchTaskHandler(context, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("nonexistent");

        // Assert
        Assert.Contains("No matches found", result);
    }

    #endregion

    #region DocumentHighlightTaskHandler Tests

    [Fact]
    public void DocumentHighlightTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_Highlight")
            .Options;
        var context = new ApplicationDbContext(options);
        var mockHighlightService = new Mock<IDocumentHighlightService>();
        var mockLogger = new Mock<ILogger<DocumentHighlightTaskHandler>>();
        var handler = new DocumentHighlightTaskHandler(context, mockHighlightService.Object, mockLogger.Object);

        // Assert
        Assert.Equal("document-highlight", handler.Name);
    }

    [Fact]
    public async Task DocumentHighlightTaskHandler_Returns_Error_Without_DocumentId()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_HighlightNoId_" + Guid.NewGuid())
            .Options;
        using var context = new ApplicationDbContext(options);
        var mockHighlightService = new Mock<IDocumentHighlightService>();
        var mockLogger = new Mock<ILogger<DocumentHighlightTaskHandler>>();
        var handler = new DocumentHighlightTaskHandler(context, mockHighlightService.Object, mockLogger.Object);

        // Act
        var result = await handler.HandleAsync("highlight some text");

        // Assert
        Assert.Contains("provide a document ID", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocumentHighlightTaskHandler_Handles_Missing_Document()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_HighlightMissing_" + Guid.NewGuid())
            .Options;
        using var context = new ApplicationDbContext(options);
        var mockHighlightService = new Mock<IDocumentHighlightService>();
        var mockLogger = new Mock<ILogger<DocumentHighlightTaskHandler>>();
        var handler = new DocumentHighlightTaskHandler(context, mockHighlightService.Object, mockLogger.Object);

        var docId = Guid.NewGuid();

        // Act
        var result = await handler.HandleAsync($"highlight test in document {docId}");

        // Assert
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region OCRTaskHandler Tests

    [Fact]
    public void OCRTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OCRTaskHandler>>();
        var mockProcessingService = new Mock<IDocumentProcessingService>();
        var mockRagService = new Mock<IRAGService>();
        var handler = new OCRTaskHandler(mockLogger.Object, mockProcessingService.Object, mockRagService.Object);

        // Assert
        Assert.Equal("ocr", handler.Name);
    }

    [Fact]
    public async Task OCRTaskHandler_Returns_Instructions_For_Invalid_Input()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OCRTaskHandler>>();
        var mockProcessingService = new Mock<IDocumentProcessingService>();
        var mockRagService = new Mock<IRAGService>();
        var handler = new OCRTaskHandler(mockLogger.Object, mockProcessingService.Object, mockRagService.Object);

        // Act
        var result = await handler.HandleAsync("invalid input");

        // Assert
        Assert.Contains("Tesseract", result);
        Assert.Contains("Usage", result);
    }

    [Fact]
    public async Task OCRTaskHandler_Processes_Valid_DocumentId()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OCRTaskHandler>>();
        var mockProcessingService = new Mock<IDocumentProcessingService>();
        var mockRagService = new Mock<IRAGService>();

        var docId = Guid.NewGuid();
        var metadata = new DocumentMetadata
        {
            Id = docId,
            FileName = "test.pdf",
            ProcessedAt = DateTime.UtcNow
        };
        metadata.Chunks.Add(new DocumentChunk { ChunkText = "test", ChunkIndex = 0, PageNumber = 1 });

        mockProcessingService.Setup(p => p.ReprocessDocumentAsync(docId))
            .ReturnsAsync(metadata);
        mockRagService.Setup(r => r.GenerateEmbeddingsAsync(docId))
            .Returns(Task.CompletedTask);

        var handler = new OCRTaskHandler(mockLogger.Object, mockProcessingService.Object, mockRagService.Object);

        // Act
        var result = await handler.HandleAsync(docId.ToString());

        // Assert
        Assert.Contains("Manual OCR retry complete", result);
        Assert.Contains(docId.ToString(), result);
    }

    [Fact]
    public async Task OCRTaskHandler_Handles_Error()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OCRTaskHandler>>();
        var mockProcessingService = new Mock<IDocumentProcessingService>();
        var mockRagService = new Mock<IRAGService>();

        var docId = Guid.NewGuid();
        mockProcessingService.Setup(p => p.ReprocessDocumentAsync(docId))
            .ThrowsAsync(new Exception("Processing error"));

        var handler = new OCRTaskHandler(mockLogger.Object, mockProcessingService.Object, mockRagService.Object);

        // Act
        var result = await handler.HandleAsync(docId.ToString());

        // Assert
        Assert.Contains("OCR error", result);
        Assert.Contains("Processing error", result);
    }

    #endregion

    #region TemplateTaskHandler Tests

    [Fact]
    public void TemplateTaskHandler_Has_Correct_Name()
    {
        // Arrange
        var handler = new TemplateTaskHandler();

        // Assert
        Assert.Equal("template", handler.Name);
    }

    [Fact]
    public async Task TemplateTaskHandler_Returns_Template_Response()
    {
        // Arrange
        var handler = new TemplateTaskHandler();

        // Act
        var result = await handler.HandleAsync("test input");

        // Assert
        Assert.Contains("template", result, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
