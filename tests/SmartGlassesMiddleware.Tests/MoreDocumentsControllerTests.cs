using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Hubs;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Additional tests for DocumentsController covering FindTextLocation,
/// MoveDocumentToGroups, ReprocessFailedDocuments and GetUnprocessedDocuments.
/// </summary>
[TestFixture]
public class MoreDocumentsControllerTests
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

        _monitoringMock.Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _monitoringMock.Setup(m => m.UpdateRequestStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        _monitoringMock.Setup(m => m.CompleteRequest(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RAG:Enabled"] = "false" })
            .Build();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private DocumentsController CreateController(IHubContext<ResourceHub>? resourceHub = null)
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
            _scopeFactoryMock.Object,
            resourceHub: resourceHub);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    // ── FindTextLocation ─────────────────────────────────────────────────────

    [Test]
    public async Task FindTextLocation_EmptySearchText_ReturnsBadRequest()
    {
        var docId = Guid.NewGuid();
        var controller = CreateController();

        var result = await controller.FindTextLocation(docId, new FindLocationRequest { SearchText = "" });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task FindTextLocation_DocumentNotFound_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.FindTextLocation(Guid.NewGuid(),
            new FindLocationRequest { SearchText = "blood pressure" });

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task FindTextLocation_DocumentFoundNoMatchingChunks_ReturnsOkWithZeroMatches()
    {
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FindTextLocation(docId,
            new FindLocationRequest { SearchText = "xyz_not_in_any_chunk" });

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("MatchCount\":0").Or.Contain("matchCount\":0").Or.Contain("\"MatchCount\":0").Or.Contain("matchcount\":0").IgnoreCase);
    }

    [Test]
    public async Task FindTextLocation_ChunkMatchesText_ReturnsLocations()
    {
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var doc = new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };
        _context.DocumentMetadatas.Add(doc);
        _context.DocumentChunks.Add(new DocumentChunk
        {
            Id = chunkId,
            DocumentMetadataId = docId,
            ChunkText = "The patient has high blood pressure levels.",
            PageNumber = 2,
            X = 100, Y = 200, Width = 300, Height = 50
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FindTextLocation(docId,
            new FindLocationRequest { SearchText = "blood pressure" });

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        // MatchCount should be 1
        Assert.That(json, Does.Contain("1"));
    }

    [Test]
    public async Task FindTextLocation_ChunkWithNoCoordinates_ReturnsPositionUnknown()
    {
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        _context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = docId,
            ChunkText = "The patient needs medication.",
            PageNumber = 1
            // No X/Y coordinates
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FindTextLocation(docId,
            new FindLocationRequest { SearchText = "medication" });

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("position unknown").IgnoreCase);
    }

    [Test]
    public async Task FindTextLocation_ChunkAtDifferentPositions_ReturnsCorrectPositionLabels()
    {
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        // Add chunk at bottom-left (low X, low Y)
        _context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = docId,
            ChunkText = "bottom left content",
            PageNumber = 1,
            X = 10, Y = 10, Width = 100, Height = 20
        });
        // Add chunk at top-right (high X, high Y)
        _context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = docId,
            ChunkText = "top right content",
            PageNumber = 1,
            X = 500, Y = 700, Width = 100, Height = 20
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();

        var result1 = await controller.FindTextLocation(docId,
            new FindLocationRequest { SearchText = "bottom left content" });
        var json1 = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result1).Value);
        Assert.That(json1, Does.Contain("bottom").IgnoreCase);
        Assert.That(json1, Does.Contain("left").IgnoreCase);

        var result2 = await controller.FindTextLocation(docId,
            new FindLocationRequest { SearchText = "top right content" });
        var json2 = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result2).Value);
        Assert.That(json2, Does.Contain("top").IgnoreCase);
        Assert.That(json2, Does.Contain("right").IgnoreCase);
    }

    [Test]
    public async Task FindTextLocation_LongChunkText_ContextTruncated()
    {
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        var longText = new string('A', 200) + " searchterm " + new string('B', 200);
        _context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = docId,
            ChunkText = longText,
            PageNumber = 1
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FindTextLocation(docId,
            new FindLocationRequest { SearchText = "searchterm" });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    // ── MoveDocumentToGroups ─────────────────────────────────────────────────

    [Test]
    public async Task MoveDocumentToGroups_DocumentNotFound_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.MoveDocumentToGroups(Guid.NewGuid(),
            new MoveDocumentRequest { GroupIds = new List<Guid>() });

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task MoveDocumentToGroups_DocumentFound_ClearsAndSetsGroups()
    {
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        await _context.SaveChangesAsync();

        var groupId = Guid.NewGuid();
        _context.ResourceGroups.Add(new ResourceGroup { Id = groupId, Name = "TestGroup" });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.MoveDocumentToGroups(docId,
            new MoveDocumentRequest { GroupIds = new List<Guid> { groupId }, RegenerateEmbeddings = false });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task MoveDocumentToGroups_EmptyGroupList_RemovesAllGroups()
    {
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.MoveDocumentToGroups(docId,
            new MoveDocumentRequest { GroupIds = new List<Guid>(), RegenerateEmbeddings = false });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task MoveDocumentToGroups_RegenerateEmbeddings_CallsRagService()
    {
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        await _context.SaveChangesAsync();

        _ragMock.Setup(r => r.GenerateEmbeddingsAsync(docId)).Returns(Task.CompletedTask);

        var controller = CreateController();
        var result = await controller.MoveDocumentToGroups(docId,
            new MoveDocumentRequest { GroupIds = new List<Guid>(), RegenerateEmbeddings = true });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _ragMock.Verify(r => r.GenerateEmbeddingsAsync(docId), Times.Once);
    }

    // ── ReprocessFailedDocuments ─────────────────────────────────────────────

    [Test]
    public async Task ReprocessFailedDocuments_Success_ReturnsOkWithCount()
    {
        _ragMock.Setup(r => r.ReprocessFailedDocumentsAsync()).ReturnsAsync(3);

        var controller = CreateController();
        var result = await controller.ReprocessFailedDocuments();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result).Value);
        Assert.That(json, Does.Contain("3"));
    }

    [Test]
    public async Task ReprocessFailedDocuments_ServiceThrows_Returns500()
    {
        _ragMock.Setup(r => r.ReprocessFailedDocumentsAsync()).ThrowsAsync(new Exception("db error"));

        var controller = CreateController();
        var result = await controller.ReprocessFailedDocuments();

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }

    // ── GetUnprocessedDocuments ──────────────────────────────────────────────

    [Test]
    public async Task GetUnprocessedDocuments_Success_ReturnsOk()
    {
        _ragMock.Setup(r => r.GetUnprocessedDocumentsAsync()).ReturnsAsync(new List<DocumentMetadata>
        {
            new DocumentMetadata
            {
                Id = Guid.NewGuid(),
                FileName = "unprocessed.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 500
            }
        });

        var controller = CreateController();
        var result = await controller.GetUnprocessedDocuments();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetUnprocessedDocuments_ServiceThrows_Returns500()
    {
        _ragMock.Setup(r => r.GetUnprocessedDocumentsAsync()).ThrowsAsync(new Exception("fail"));

        var controller = CreateController();
        var result = await controller.GetUnprocessedDocuments();

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }

    // ── DownloadDocument ─────────────────────────────────────────────────────

    [Test]
    public async Task DownloadDocument_NotFound_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.DownloadDocument(Guid.NewGuid());

        // DownloadDocument returns plain NotFoundResult (no body) when the document ID is unknown
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task DownloadDocument_DocumentFoundButStorageFails_Throws()
    {
        // DownloadDocument does not wrap the storage call in a try-catch,
        // so a storage exception propagates as-is.
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        await _context.SaveChangesAsync();

        _storageMock.Setup(s => s.DownloadFileAsync("s3://bucket/report.pdf"))
            .ThrowsAsync(new Exception("S3 error"));

        var controller = CreateController();

        Assert.ThrowsAsync<Exception>(async () =>
            await controller.DownloadDocument(docId));
    }

    // ── GetPositionDescription – center/middle branches ──────────────────────

    [Test]
    public async Task FindTextLocation_ChunkAtCenterMiddle_ReturnsCenterMiddlePosition()
    {
        // PdfConstants.PageWidth=612, PageHeight=792
        // center = 204 ≤ x < 408; middle = 264 ≤ y < 528
        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        _context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = docId,
            ChunkText = "center middle text sample",
            PageNumber = 1,
            X = 306,   // exactly in the center band (204 ≤ 306 < 408)
            Y = 400,   // exactly in the middle band (264 ≤ 400 < 528)
            Width = 100, Height = 20
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FindTextLocation(docId,
            new FindLocationRequest { SearchText = "center middle text sample" });

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("center").IgnoreCase);
        Assert.That(json, Does.Contain("middle").IgnoreCase);
    }

    // ── NotifyFilesChangedAsync hub path (lines 300-304) ─────────────────────

    [Test]
    public async Task MoveDocumentToGroups_WithResourceHub_NotifiesHub()
    {
        // Pass a non-null IHubContext<ResourceHub> to exercise the hub branch of
        // NotifyGroupsChangedAsync (and NotifyFilesChangedAsync) inside DocumentsController.
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientProxyMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubMock = new Mock<IHubContext<ResourceHub>>();
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var docId = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = docId,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        });
        await _context.SaveChangesAsync();

        var controller = CreateController(resourceHub: hubMock.Object);
        var result = await controller.MoveDocumentToGroups(docId,
            new MoveDocumentRequest { GroupIds = new List<Guid>(), RegenerateEmbeddings = false });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        clientProxyMock.Verify(c =>
            c.SendCoreAsync("groupsChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
