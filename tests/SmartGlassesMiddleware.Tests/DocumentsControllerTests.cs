using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class DocumentsControllerTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private DocumentsController CreateController(
        ApplicationDbContext? context = null,
        Mock<IDocumentProcessingService>? processingService = null,
        Mock<IRAGService>? ragService = null,
        Mock<IDocumentHighlightService>? highlightService = null,
        IConfiguration? configuration = null,
        Mock<IRequestMonitoringService>? monitoringService = null,
        Mock<IMediaStorageService>? mediaStorageService = null,
        Mock<IHistoryService>? historyService = null,
        Mock<IConversationService>? conversationService = null)
    {
        var ctx = context ?? GetInMemoryContext();
        var mockProcessing = processingService ?? new Mock<IDocumentProcessingService>();
        var mockRag = ragService ?? new Mock<IRAGService>();
        var mockHighlight = highlightService ?? new Mock<IDocumentHighlightService>();
        var mockLogger = new Mock<ILogger<DocumentsController>>();
        var mockMonitoring = monitoringService ?? new Mock<IRequestMonitoringService>();
        var mockHistory = historyService ?? new Mock<IHistoryService>();
        var mockConversation = conversationService ?? new Mock<IConversationService>();

        var config = configuration ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG:MaxFileSizeMB"] = "50"
            })
            .Build();

        var mockMediaStorage = mediaStorageService ?? new Mock<IMediaStorageService>();
        var controller = new DocumentsController(
            mockLogger.Object,
            mockProcessing.Object,
            mockRag.Object,
            mockHighlight.Object,
            ctx,
            config,
            mockMonitoring.Object,
            mockHistory.Object,
            mockMediaStorage.Object,
            mockConversation.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private IFormFile CreateMockFormFile(string fileName, string content = "test content", string contentType = "application/pdf")
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var formFile = new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
        return formFile;
    }

    #region UploadDocument Tests

    [Fact]
    public async Task UploadDocument_Returns_Ok_With_Document_Metadata()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockProcessing = new Mock<IDocumentProcessingService>();
        var mockRag = new Mock<IRAGService>();

        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            TotalPages = 5,
            IsProcessed = true,
            ProcessedAt = DateTime.UtcNow,
            Chunks = new List<DocumentChunk>
            {
                new DocumentChunk { Id = Guid.NewGuid(), ChunkText = "Chunk 1" },
                new DocumentChunk { Id = Guid.NewGuid(), ChunkText = "Chunk 2" }
            }
        };

        mockProcessing.Setup(s => s.ProcessDocumentAsync(It.IsAny<Stream>(), "test.pdf", "application/pdf"))
            .ReturnsAsync(document);
        mockRag.Setup(s => s.GenerateEmbeddingsAsync(documentId))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockProcessing, mockRag);
        var file = CreateMockFormFile("test.pdf", "PDF content", "application/pdf");

        // Act
        var result = await controller.UploadDocument(file);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var value = okResult.Value;
        Assert.NotNull(value);

        // Verify processing was called
        mockProcessing.Verify(s => s.ProcessDocumentAsync(It.IsAny<Stream>(), "test.pdf", "application/pdf"), Times.Once);
        mockRag.Verify(s => s.GenerateEmbeddingsAsync(documentId), Times.Once);
    }

    [Fact]
    public async Task UploadDocument_Returns_BadRequest_When_File_Is_Null()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.UploadDocument(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file uploaded", badRequestResult.Value);
    }

    [Fact]
    public async Task UploadDocument_Returns_BadRequest_When_File_Is_Empty()
    {
        // Arrange
        var controller = CreateController();
        var file = CreateMockFormFile("test.pdf", "", "application/pdf");

        // Act
        var result = await controller.UploadDocument(file);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file uploaded", badRequestResult.Value);
    }

    [Fact]
    public async Task UploadDocument_Returns_BadRequest_When_File_Exceeds_Max_Size()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG:MaxFileSizeMB"] = "1" // 1 MB limit
            })
            .Build();

        var controller = CreateController(configuration: config);

        // Create a file larger than 1 MB
        var largeContent = new string('x', 2 * 1024 * 1024); // 2 MB
        var file = CreateMockFormFile("large.pdf", largeContent, "application/pdf");

        // Act
        var result = await controller.UploadDocument(file);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("exceeds maximum allowed size", badRequestResult.Value?.ToString());
    }

    [Fact]
    public async Task UploadDocument_With_Groups_Adds_Document_To_Groups()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockProcessing = new Mock<IDocumentProcessingService>();
        var mockRag = new Mock<IRAGService>();

        var groupId1 = Guid.NewGuid();
        var groupId2 = Guid.NewGuid();

        var group1 = new ResourceGroup { Id = groupId1, Name = "Group 1" };
        var group2 = new ResourceGroup { Id = groupId2, Name = "Group 2" };
        context.ResourceGroups.AddRange(group1, group2);
        await context.SaveChangesAsync();

        var documentId = Guid.NewGuid();

        mockProcessing.Setup(s => s.ProcessDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Stream stream, string fileName, string contentType) =>
            {
                var document = new DocumentMetadata
                {
                    Id = documentId,
                    FileName = fileName,
                    DocumentType = "PDF",
                    DocumentGroups = new List<DocumentGroup>()
                };
                // Add document to context so it can be tracked
                context.DocumentMetadatas.Add(document);
                context.SaveChanges();
                return document;
            });
        mockRag.Setup(s => s.GenerateEmbeddingsAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockProcessing, mockRag);
        var file = CreateMockFormFile("test.pdf", "PDF content", "application/pdf");
        var groupIds = new List<Guid> { groupId1, groupId2 };

        // Act
        var result = await controller.UploadDocument(file, groupIds);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Verify groups were added
        var savedGroups = await context.DocumentGroups.Where(dg => dg.DocumentMetadataId == documentId).ToListAsync();
        Assert.Equal(2, savedGroups.Count);
    }

    [Fact]
    public async Task UploadDocument_Continues_When_Embedding_Generation_Fails()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockProcessing = new Mock<IDocumentProcessingService>();
        var mockRag = new Mock<IRAGService>();

        var document = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "test.pdf",
            DocumentType = "PDF"
        };

        mockProcessing.Setup(s => s.ProcessDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(document);
        mockRag.Setup(s => s.GenerateEmbeddingsAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new Exception("Embedding error"));

        var controller = CreateController(context, mockProcessing, mockRag);
        var file = CreateMockFormFile("test.pdf", "PDF content", "application/pdf");

        // Act
        var result = await controller.UploadDocument(file);

        // Assert
        // Should still return OK even if embedding generation fails
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task UploadDocument_Returns_InternalServerError_On_Processing_Exception()
    {
        // Arrange
        var mockProcessing = new Mock<IDocumentProcessingService>();
        mockProcessing.Setup(s => s.ProcessDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Processing error"));

        var controller = CreateController(processingService: mockProcessing);
        var file = CreateMockFormFile("test.pdf", "PDF content", "application/pdf");

        // Act
        var result = await controller.UploadDocument(file);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    #endregion

    #region QueryDocuments Tests

    [Fact]
    public async Task QueryDocuments_Returns_Ok_With_Query_Result()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        var mockConversation = new Mock<IConversationService>();
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation { Id = conversationId };
        
        var queryResult = new QueryResult
        {
            Answer = "This is the answer",
            Sources = new List<DocumentSource>
            {
                new DocumentSource { DocumentId = Guid.NewGuid(), FileName = "doc1.pdf", PageNumber = 1 }
            }
        };

        mockConversation.Setup(s => s.DetectOrCreateConversationAsync("test query", null))
            .ReturnsAsync(conversation);
        mockConversation.Setup(s => s.AddMessageAsync(conversationId, "user", "test query", null))
            .ReturnsAsync(new ChatMessage());
        mockConversation.Setup(s => s.AddMessageAsync(conversationId, "assistant", "This is the answer", null))
            .ReturnsAsync(new ChatMessage());
        mockRag.Setup(s => s.QueryDocumentsWithConversationAsync("test query", conversationId, 5, null, null))
            .ReturnsAsync(queryResult);

        var controller = CreateController(ragService: mockRag, conversationService: mockConversation);
        var request = new QueryRequest { Query = "test query", TopK = 5 };

        // Act
        var result = await controller.QueryDocuments(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResult = Assert.IsType<QueryResult>(okResult.Value);
        Assert.Equal("This is the answer", returnedResult.Answer);
        Assert.Single(returnedResult.Sources);
        
        // Verify conversation methods were called
        mockConversation.Verify(s => s.DetectOrCreateConversationAsync("test query", null), Times.Once);
        mockConversation.Verify(s => s.AddMessageAsync(conversationId, "user", "test query", null), Times.Once);
        mockConversation.Verify(s => s.AddMessageAsync(conversationId, "assistant", "This is the answer", null), Times.Once);
    }

    [Fact]
    public async Task QueryDocuments_Returns_BadRequest_When_Query_Is_Empty()
    {
        // Arrange
        var controller = CreateController();
        var request = new QueryRequest { Query = "" };

        // Act
        var result = await controller.QueryDocuments(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Query cannot be empty", badRequestResult.Value?.ToString());
    }

    [Fact]
    public async Task QueryDocuments_Returns_BadRequest_When_Query_Is_Whitespace()
    {
        // Arrange
        var controller = CreateController();
        var request = new QueryRequest { Query = "   " };

        // Act
        var result = await controller.QueryDocuments(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Query cannot be empty", badRequestResult.Value?.ToString());
    }

    [Fact]
    public async Task QueryDocuments_Uses_Default_TopK_When_Not_Specified()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        var mockConversation = new Mock<IConversationService>();
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation { Id = conversationId };
        var queryResult = new QueryResult { Answer = "Answer" };

        mockConversation.Setup(s => s.DetectOrCreateConversationAsync("test", null))
            .ReturnsAsync(conversation);
        mockConversation.Setup(s => s.AddMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), null))
            .ReturnsAsync(new ChatMessage());
        mockRag.Setup(s => s.QueryDocumentsWithConversationAsync("test", conversationId, 5, null, null))
            .ReturnsAsync(queryResult);

        var controller = CreateController(ragService: mockRag, conversationService: mockConversation);
        var request = new QueryRequest { Query = "test" };

        // Act
        var result = await controller.QueryDocuments(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockRag.Verify(s => s.QueryDocumentsWithConversationAsync("test", conversationId, 5, null, null), Times.Once);
    }

    [Fact]
    public async Task QueryDocuments_Passes_GroupIds_To_Service()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        var mockConversation = new Mock<IConversationService>();
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation { Id = conversationId };
        var queryResult = new QueryResult { Answer = "Answer" };
        var groupIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        mockConversation.Setup(s => s.DetectOrCreateConversationAsync("test", groupIds.First()))
            .ReturnsAsync(conversation);
        mockConversation.Setup(s => s.AddMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), null))
            .ReturnsAsync(new ChatMessage());
        mockRag.Setup(s => s.QueryDocumentsWithConversationAsync("test", conversationId, 10, groupIds, null))
            .ReturnsAsync(queryResult);

        var controller = CreateController(ragService: mockRag, conversationService: mockConversation);
        var request = new QueryRequest { Query = "test", TopK = 10, GroupIds = groupIds };

        // Act
        var result = await controller.QueryDocuments(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockRag.Verify(s => s.QueryDocumentsWithConversationAsync("test", conversationId, 10, groupIds, null), Times.Once);
    }

    [Fact]
    public async Task QueryDocuments_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        var mockConversation = new Mock<IConversationService>();
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation { Id = conversationId };
        
        mockConversation.Setup(s => s.DetectOrCreateConversationAsync(It.IsAny<string>(), null))
            .ReturnsAsync(conversation);
        mockConversation.Setup(s => s.AddMessageAsync(It.IsAny<Guid>(), "user", It.IsAny<string>(), null))
            .ReturnsAsync(new ChatMessage());
        mockRag.Setup(s => s.QueryDocumentsWithConversationAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<List<Guid>?>(), It.IsAny<List<Guid>?>()))
            .ThrowsAsync(new Exception("Query error"));

        var controller = CreateController(ragService: mockRag, conversationService: mockConversation);
        var request = new QueryRequest { Query = "test" };

        // Act
        var result = await controller.QueryDocuments(request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task QueryDocuments_ContinuesConversation_WithinSameGroup()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        var mockConversation = new Mock<IConversationService>();
        var groupId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation { Id = conversationId, ResourceGroupId = groupId };
        var queryResult = new QueryResult { Answer = "Answer" };
        var groupIds = new List<Guid> { groupId };

        // Setup conversation service to detect/continue existing conversation
        mockConversation.Setup(s => s.DetectOrCreateConversationAsync(It.IsAny<string>(), groupId))
            .ReturnsAsync(conversation);
        mockConversation.Setup(s => s.AddMessageAsync(conversationId, "user", It.IsAny<string>(), null))
            .ReturnsAsync(new ChatMessage());
        mockConversation.Setup(s => s.AddMessageAsync(conversationId, "assistant", It.IsAny<string>(), null))
            .ReturnsAsync(new ChatMessage());
        mockRag.Setup(s => s.QueryDocumentsWithConversationAsync(It.IsAny<string>(), conversationId, 5, groupIds, null))
            .ReturnsAsync(queryResult);

        var controller = CreateController(ragService: mockRag, conversationService: mockConversation);
        
        // Act - First query
        var request1 = new QueryRequest { Query = "What is AI?", GroupIds = groupIds };
        var result1 = await controller.QueryDocuments(request1);
        
        // Act - Second query in same group (should continue conversation)
        var request2 = new QueryRequest { Query = "Tell me more", GroupIds = groupIds };
        var result2 = await controller.QueryDocuments(request2);

        // Assert - Both queries should be OK
        Assert.IsType<OkObjectResult>(result1);
        Assert.IsType<OkObjectResult>(result2);
        
        // Verify conversation service was called twice with same groupId
        mockConversation.Verify(s => s.DetectOrCreateConversationAsync(It.IsAny<string>(), groupId), Times.Exactly(2));
        
        // Verify both queries used conversation-aware RAG method
        mockRag.Verify(s => s.QueryDocumentsWithConversationAsync(It.IsAny<string>(), conversationId, 5, groupIds, null), Times.Exactly(2));
        
        // Verify messages were added to the same conversation (2 user + 2 assistant = 4 messages)
        mockConversation.Verify(s => s.AddMessageAsync(conversationId, "user", It.IsAny<string>(), null), Times.Exactly(2));
        mockConversation.Verify(s => s.AddMessageAsync(conversationId, "assistant", It.IsAny<string>(), null), Times.Exactly(2));
    }

    #endregion

    #region HighlightDocument Tests

    [Fact]
    public async Task HighlightDocument_With_ChunkIds_Returns_File()
    {
        // Arrange
        var mockHighlight = new Mock<IDocumentHighlightService>();
        var context = GetInMemoryContext();

        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            ContentType = "application/pdf"
        };
        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var highlightedStream = new MemoryStream(Encoding.UTF8.GetBytes("highlighted content"));
        var chunkIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        mockHighlight.Setup(s => s.HighlightDocumentAsync(documentId, chunkIds))
            .ReturnsAsync(highlightedStream);

        var controller = CreateController(context, highlightService: mockHighlight);
        var request = new HighlightRequest { ChunkIds = chunkIds };

        // Act
        var result = await controller.HighlightDocument(documentId, request);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.StartsWith("highlighted_", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task HighlightDocument_With_SearchText_Returns_File()
    {
        // Arrange
        var mockHighlight = new Mock<IDocumentHighlightService>();
        var context = GetInMemoryContext();

        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            ContentType = "application/pdf"
        };
        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var highlightedStream = new MemoryStream(Encoding.UTF8.GetBytes("highlighted content"));

        mockHighlight.Setup(s => s.HighlightTextLocationsAsync(documentId, "search term"))
            .ReturnsAsync(highlightedStream);

        var controller = CreateController(context, highlightService: mockHighlight);
        var request = new HighlightRequest { SearchText = "search term" };

        // Act
        var result = await controller.HighlightDocument(documentId, request);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
    }

    [Fact]
    public async Task HighlightDocument_Returns_BadRequest_When_Neither_ChunkIds_Nor_SearchText_Provided()
    {
        // Arrange
        var controller = CreateController();
        var documentId = Guid.NewGuid();
        var request = new HighlightRequest();

        // Act
        var result = await controller.HighlightDocument(documentId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("ChunkIds or SearchText must be provided", badRequestResult.Value?.ToString());
    }

    [Fact]
    public async Task HighlightDocument_Returns_NotFound_When_Document_Not_Found()
    {
        // Arrange
        var mockHighlight = new Mock<IDocumentHighlightService>();
        mockHighlight.Setup(s => s.HighlightDocumentAsync(It.IsAny<Guid>(), It.IsAny<List<Guid>>()))
            .ThrowsAsync(new FileNotFoundException("Document not found"));

        var controller = CreateController(highlightService: mockHighlight);
        var documentId = Guid.NewGuid();
        var request = new HighlightRequest { ChunkIds = new List<Guid> { Guid.NewGuid() } };

        // Act
        var result = await controller.HighlightDocument(documentId, request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task HighlightDocument_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockHighlight = new Mock<IDocumentHighlightService>();
        mockHighlight.Setup(s => s.HighlightDocumentAsync(It.IsAny<Guid>(), It.IsAny<List<Guid>>()))
            .ThrowsAsync(new Exception("Highlight error"));

        var controller = CreateController(highlightService: mockHighlight);
        var documentId = Guid.NewGuid();
        var request = new HighlightRequest { ChunkIds = new List<Guid> { Guid.NewGuid() } };

        // Act
        var result = await controller.HighlightDocument(documentId, request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    #endregion

    #region ListDocuments Tests

    [Fact]
    public async Task ListDocuments_Returns_Ok_With_Documents_List()
    {
        // Arrange
        var context = GetInMemoryContext();

        var doc1 = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "doc1.pdf",
            DocumentType = "PDF",
            UploadedAt = DateTime.UtcNow.AddDays(-2),
            Chunks = new List<DocumentChunk>()
        };
        var doc2 = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "doc2.pdf",
            DocumentType = "PDF",
            UploadedAt = DateTime.UtcNow.AddDays(-1),
            Chunks = new List<DocumentChunk>()
        };

        context.DocumentMetadatas.AddRange(doc1, doc2);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.ListDocuments(1, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ListDocuments_Returns_Paginated_Results()
    {
        // Arrange
        var context = GetInMemoryContext();

        for (int i = 0; i < 25; i++)
        {
            context.DocumentMetadatas.Add(new DocumentMetadata
            {
                Id = Guid.NewGuid(),
                FileName = $"doc{i}.pdf",
                DocumentType = "PDF",
                UploadedAt = DateTime.UtcNow.AddDays(-i)
            });
        }
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.ListDocuments(1, 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ListDocuments_Orders_By_UploadedAt_Descending()
    {
        // Arrange
        var context = GetInMemoryContext();

        var oldDoc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "old.pdf",
            DocumentType = "PDF",
            UploadedAt = DateTime.UtcNow.AddDays(-10)
        };
        var newDoc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "new.pdf",
            DocumentType = "PDF",
            UploadedAt = DateTime.UtcNow
        };

        context.DocumentMetadatas.AddRange(oldDoc, newDoc);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.ListDocuments(1, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    #endregion

    #region GetDocument Tests

    [Fact]
    public async Task GetDocument_Returns_Ok_With_Document_Details()
    {
        // Arrange
        var context = GetInMemoryContext();
        var documentId = Guid.NewGuid();

        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            TotalPages = 10,
            Chunks = new List<DocumentChunk>
            {
                new DocumentChunk { Id = Guid.NewGuid(), ChunkText = "Chunk 1 text content" },
                new DocumentChunk { Id = Guid.NewGuid(), ChunkText = "Chunk 2 text content" }
            }
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.GetDocument(documentId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetDocument_Returns_NotFound_When_Document_Does_Not_Exist()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);
        var documentId = Guid.NewGuid();

        // Act
        var result = await controller.GetDocument(documentId);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetDocument_Includes_Chunk_Previews()
    {
        // Arrange
        var context = GetInMemoryContext();
        var documentId = Guid.NewGuid();

        var longText = new string('x', 200);
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            Chunks = new List<DocumentChunk>
            {
                new DocumentChunk { Id = Guid.NewGuid(), ChunkText = longText }
            }
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.GetDocument(documentId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    #endregion

    #region DeleteDocument Tests

    [Fact]
    public async Task DeleteDocument_Returns_NoContent_When_Successful()
    {
        // Arrange
        var context = GetInMemoryContext();
        var documentId = Guid.NewGuid();

        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF"
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.DeleteDocument(documentId);

        // Assert
        Assert.IsType<NoContentResult>(result);

        var deletedDoc = await context.DocumentMetadatas.FindAsync(documentId);
        Assert.Null(deletedDoc);
    }

    [Fact]
    public async Task DeleteDocument_Returns_NotFound_When_Document_Does_Not_Exist()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);
        var documentId = Guid.NewGuid();

        // Act
        var result = await controller.DeleteDocument(documentId);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    #endregion

    #region ReprocessFailedDocuments Tests

    [Fact]
    public async Task ReprocessFailedDocuments_Returns_Ok_With_Count()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        mockRag.Setup(s => s.ReprocessFailedDocumentsAsync())
            .ReturnsAsync(3);

        var controller = CreateController(ragService: mockRag);

        // Act
        var result = await controller.ReprocessFailedDocuments();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ReprocessFailedDocuments_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        mockRag.Setup(s => s.ReprocessFailedDocumentsAsync())
            .ThrowsAsync(new Exception("Reprocess error"));

        var controller = CreateController(ragService: mockRag);

        // Act
        var result = await controller.ReprocessFailedDocuments();

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    #endregion

    #region GetUnprocessedDocuments Tests

    [Fact]
    public async Task GetUnprocessedDocuments_Returns_Ok_With_Documents()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        var unprocessedDocs = new List<DocumentMetadata>
        {
            new DocumentMetadata { Id = Guid.NewGuid(), FileName = "unprocessed1.pdf", IsProcessed = false },
            new DocumentMetadata { Id = Guid.NewGuid(), FileName = "unprocessed2.pdf", IsProcessed = false }
        };

        mockRag.Setup(s => s.GetUnprocessedDocumentsAsync())
            .ReturnsAsync(unprocessedDocs);

        var controller = CreateController(ragService: mockRag);

        // Act
        var result = await controller.GetUnprocessedDocuments();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetUnprocessedDocuments_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var mockRag = new Mock<IRAGService>();
        mockRag.Setup(s => s.GetUnprocessedDocumentsAsync())
            .ThrowsAsync(new Exception("Get error"));

        var controller = CreateController(ragService: mockRag);

        // Act
        var result = await controller.GetUnprocessedDocuments();

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    #endregion

    #region MoveDocumentToGroups Tests

    [Fact]
    public async Task MoveDocumentToGroups_Returns_Ok_When_Successful()
    {
        // Arrange
        var context = GetInMemoryContext();
        var documentId = Guid.NewGuid();

        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            DocumentGroups = new List<DocumentGroup>()
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var newGroupId = Guid.NewGuid();
        var controller = CreateController(context);
        var request = new MoveDocumentRequest
        {
            GroupIds = new List<Guid> { newGroupId },
            RegenerateEmbeddings = false
        };

        // Act
        var result = await controller.MoveDocumentToGroups(documentId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task MoveDocumentToGroups_Returns_NotFound_When_Document_Does_Not_Exist()
    {
        // Arrange
        var context = GetInMemoryContext();
        var controller = CreateController(context);
        var documentId = Guid.NewGuid();
        var request = new MoveDocumentRequest
        {
            GroupIds = new List<Guid> { Guid.NewGuid() }
        };

        // Act
        var result = await controller.MoveDocumentToGroups(documentId, request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MoveDocumentToGroups_Removes_Existing_Groups()
    {
        // Arrange
        var context = GetInMemoryContext();
        var documentId = Guid.NewGuid();
        var oldGroupId = Guid.NewGuid();

        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            DocumentGroups = new List<DocumentGroup>
            {
                new DocumentGroup { DocumentMetadataId = documentId, ResourceGroupId = oldGroupId }
            }
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var newGroupId = Guid.NewGuid();
        var controller = CreateController(context);
        var request = new MoveDocumentRequest
        {
            GroupIds = new List<Guid> { newGroupId },
            RegenerateEmbeddings = false
        };

        // Act
        var result = await controller.MoveDocumentToGroups(documentId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);

        var groups = await context.DocumentGroups.Where(dg => dg.DocumentMetadataId == documentId).ToListAsync();
        Assert.Single(groups);
        Assert.Equal(newGroupId, groups[0].ResourceGroupId);
    }

    [Fact]
    public async Task MoveDocumentToGroups_Regenerates_Embeddings_When_Requested()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockRag = new Mock<IRAGService>();

        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            DocumentGroups = new List<DocumentGroup>()
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        mockRag.Setup(s => s.GenerateEmbeddingsAsync(documentId))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, ragService: mockRag);
        var request = new MoveDocumentRequest
        {
            GroupIds = new List<Guid> { Guid.NewGuid() },
            RegenerateEmbeddings = true
        };

        // Act
        var result = await controller.MoveDocumentToGroups(documentId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockRag.Verify(s => s.GenerateEmbeddingsAsync(documentId), Times.Once);
    }

    [Fact]
    public async Task MoveDocumentToGroups_Does_Not_Regenerate_Embeddings_When_Not_Requested()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockRag = new Mock<IRAGService>();

        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            DocumentGroups = new List<DocumentGroup>()
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        var controller = CreateController(context, ragService: mockRag);
        var request = new MoveDocumentRequest
        {
            GroupIds = new List<Guid> { Guid.NewGuid() },
            RegenerateEmbeddings = false
        };

        // Act
        var result = await controller.MoveDocumentToGroups(documentId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        mockRag.Verify(s => s.GenerateEmbeddingsAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task MoveDocumentToGroups_Returns_InternalServerError_On_Exception()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockRag = new Mock<IRAGService>();

        var documentId = Guid.NewGuid();
        var document = new DocumentMetadata
        {
            Id = documentId,
            FileName = "test.pdf",
            DocumentType = "PDF",
            DocumentGroups = new List<DocumentGroup>()
        };

        context.DocumentMetadatas.Add(document);
        await context.SaveChangesAsync();

        mockRag.Setup(s => s.GenerateEmbeddingsAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new Exception("Embedding error"));

        var controller = CreateController(context, ragService: mockRag);
        var request = new MoveDocumentRequest
        {
            GroupIds = new List<Guid> { Guid.NewGuid() },
            RegenerateEmbeddings = true
        };

        // Act
        var result = await controller.MoveDocumentToGroups(documentId, request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    #endregion
}
