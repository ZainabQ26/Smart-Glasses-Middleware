using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class DocumentsControllerTests
{
    private ApplicationDbContext _context = null!;
    private Mock<ILogger<DocumentsController>> _loggerMock = null!;
    private Mock<IDocumentProcessingService> _processingMock = null!;
    private Mock<IRAGService> _ragMock = null!;
    private Mock<IDocumentHighlightService> _highlightMock = null!;
    private Mock<IRequestMonitoringService> _monitoringMock = null!;
    private Mock<IHistoryService> _historyMock = null!;
    private Mock<IMediaStorageService> _storageMock = null!;
    private Mock<IConversationService> _conversationMock = null!;
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;
    private IConfiguration _config = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _loggerMock = new Mock<ILogger<DocumentsController>>();
        _processingMock = new Mock<IDocumentProcessingService>();
        _ragMock = new Mock<IRAGService>();
        _highlightMock = new Mock<IDocumentHighlightService>();
        _monitoringMock = new Mock<IRequestMonitoringService>();
        _historyMock = new Mock<IHistoryService>();
        _storageMock = new Mock<IMediaStorageService>();
        _conversationMock = new Mock<IConversationService>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        _monitoringMock
            .Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _monitoringMock
            .Setup(m => m.UpdateRequestStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        _monitoringMock
            .Setup(m => m.CompleteRequest(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG:Enabled"] = "false"
            })
            .Build();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    private DocumentsController CreateController()
    {
        var controller = new DocumentsController(
            _loggerMock.Object,
            _processingMock.Object,
            _ragMock.Object,
            _highlightMock.Object,
            _context,
            _config,
            _monitoringMock.Object,
            _historyMock.Object,
            _storageMock.Object,
            _conversationMock.Object,
            _scopeFactoryMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    // ─── ListDocuments ───────────────────────────────────────────────────────

    [Test]
    public async Task GetDocuments_EmptyDb_ReturnsOkWithEmptyList()
    {
        var controller = CreateController();

        var result = await controller.ListDocuments();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetDocuments_WithDocument_ReturnsOk()
    {
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "test.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/test.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024,
            IsProcessed = true
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.ListDocuments();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetDocuments_DocWithNoStorageKey_NotReturned()
    {
        // ListDocuments filters for non-empty StorageKey and FileSize > 0;
        // a document with empty StorageKey and FileSize=0 is excluded from the result.
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "pending.pdf",
            ContentType = "application/pdf",
            StorageKey = "",
            UploadedAt = DateTime.UtcNow,
            FileSize = 0,
            IsProcessed = false
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.ListDocuments();

        // The controller returns 200 OK in all cases; the serialized result will
        // have TotalCount=0 because the pending document is filtered out.
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.That(json, Does.Contain("\"TotalCount\":0"));
    }

    // ─── GetDocument ─────────────────────────────────────────────────────────

    [Test]
    public async Task GetDocument_NotFound_Returns404()
    {
        var controller = CreateController();

        var result = await controller.GetDocument(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetDocument_Found_ReturnsOk()
    {
        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "found.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/found.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 512,
            IsProcessed = true
        };
        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetDocument(doc.Id);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    // ─── DeleteDocument ───────────────────────────────────────────────────────

    [Test]
    public async Task DeleteDocument_NotFound_Returns404()
    {
        var controller = CreateController();

        var result = await controller.DeleteDocument(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task DeleteDocument_Found_Returns204()
    {
        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "todelete.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/todelete.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 256,
            IsProcessed = true
        };
        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.DeleteDocument(doc.Id);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(await _context.DocumentMetadatas.FindAsync(doc.Id), Is.Null);
    }

    // ─── QueryDocuments ───────────────────────────────────────────────────────

    [Test]
    public async Task QueryDocuments_EmptyQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new QueryRequest { Query = "" };

        var result = await controller.QueryDocuments(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task QueryDocuments_WhitespaceQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new QueryRequest { Query = "   " };

        var result = await controller.QueryDocuments(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task QueryDocuments_ValidQuery_ReturnsOk()
    {
        var queryResult = new QueryResult
        {
            Answer = "This is the answer.",
            Sources = new List<DocumentSource>()
        };

        _ragMock.Setup(r => r.QueryDocumentsAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<List<Guid>?>(),
                It.IsAny<List<Guid>?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(queryResult);

        var controller = CreateController();
        var request = new QueryRequest { Query = "What is in the document?" };

        var result = await controller.QueryDocuments(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        Assert.That(ok.Value, Is.Not.Null);
    }
}
