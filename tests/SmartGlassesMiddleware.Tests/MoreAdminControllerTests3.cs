using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Tests targeting AdminController endpoints not yet covered:
/// FixProcessedManagedFiles, FileStatus, FileInfo, DownloadAbbreviationsCsv, DeleteFile.
/// </summary>
[TestFixture]
public class MoreAdminControllerTests3
{
    private ApplicationDbContext _context = null!;
    private Mock<IMediaStorageService> _mediaStorageMock = null!;
    private Mock<ICacheService> _cacheMock = null!;
    private Mock<IRagService> _ragServiceMock = null!;
    private Mock<IRAGService> _docRagServiceMock = null!;
    private Mock<ILogger<AdminController>> _loggerMock = null!;
    private Mock<IRequestMonitoringService> _monitoringMock = null!;
    private Mock<IServerSettingsService> _settingsMock = null!;
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _mediaStorageMock = new Mock<IMediaStorageService>();
        _cacheMock = new Mock<ICacheService>();
        _ragServiceMock = new Mock<IRagService>();
        _docRagServiceMock = new Mock<IRAGService>();
        _loggerMock = new Mock<ILogger<AdminController>>();
        _monitoringMock = new Mock<IRequestMonitoringService>();
        _settingsMock = new Mock<IServerSettingsService>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        _cacheMock.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);
        _cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        _monitoringMock.Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _monitoringMock.Setup(m => m.CompleteRequest(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _monitoringMock.Setup(m => m.UpdateRequestStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private AdminController CreateController()
    {
        var controller = new AdminController(
            _context,
            _mediaStorageMock.Object,
            _cacheMock.Object,
            _ragServiceMock.Object,
            _docRagServiceMock.Object,
            _loggerMock.Object,
            _monitoringMock.Object,
            _settingsMock.Object,
            _scopeFactoryMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    // ── FixProcessedManagedFiles ─────────────────────────────────────────────

    [Test]
    public async Task FixProcessedManagedFiles_NoProcessedDocs_ReturnsOkWithZeroUpdated()
    {
        var controller = CreateController();
        var result = await controller.FixProcessedManagedFiles();

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("0"));
    }

    [Test]
    public async Task FixProcessedManagedFiles_ProcessedDocWithManagedFile_UpdatesContentType()
    {
        var mfId = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = mfId,
            FileName = "original.docx",
            ContentType = "application/msword",
            StorageKey = "s3://bucket/original.docx",
            UploadedAt = DateTime.UtcNow,
            FileSize = 2048
        });
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "converted.pdf",
            ManagedFileId = mfId,
            IsProcessed = true,
            StorageKey = "s3://bucket/converted.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 2048
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FixProcessedManagedFiles();

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        // Should have updated 1 file
        Assert.That(json, Does.Contain("1"));

        // Verify content type was updated
        var updatedFile = await _context.ManagedFiles.FindAsync(mfId);
        Assert.That(updatedFile!.ContentType, Is.EqualTo("application/pdf"));
    }

    [Test]
    public async Task FixProcessedManagedFiles_AlreadyPdfContentType_NotCounted()
    {
        var mfId = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = mfId,
            FileName = "report.pdf",
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024
        });
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "report.pdf",
            ManagedFileId = mfId,
            IsProcessed = true,
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FixProcessedManagedFiles();

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        // 0 updated since content type and filename already match
        Assert.That(json, Does.Contain("\"updated\":0"));
    }

    // ── FileStatus ───────────────────────────────────────────────────────────

    [Test]
    public async Task FileStatus_ManagedFileReady_ReturnsOkWithReadyTrue()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/doc.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FileStatus(id);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("true")); // isReady = true
    }

    [Test]
    public async Task FileStatus_ManagedFilePending_ReturnsOkWithPendingTrue()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            StorageKey = "PENDING:upload-123",
            UploadedAt = DateTime.UtcNow,
            FileSize = 0
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FileStatus(id);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("PENDING").IgnoreCase);
    }

    [Test]
    public async Task FileStatus_ManagedFileFailed_ReturnsOkWithFailedTrue()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            StorageKey = "FAILED:upload-error",
            UploadedAt = DateTime.UtcNow,
            FileSize = 0
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FileStatus(id);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("FAILED").IgnoreCase);
    }

    [Test]
    public async Task FileStatus_DocumentMetadataFound_ReturnsOk()
    {
        var id = Guid.NewGuid();
        // No ManagedFile, but DocumentMetadata exists
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = id,
            FileName = "report.pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 2048,
            IsProcessed = true
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FileStatus(id);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        Assert.That(json, Does.Contain("report.pdf"));
    }

    [Test]
    public async Task FileStatus_NotFoundAnywhere_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.FileStatus(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    // ── FileInfo ─────────────────────────────────────────────────────────────

    [Test]
    public async Task FileInfo_NotFoundAnywhere_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.FileInfo(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task FileInfo_ManagedFilePending_Returns409Conflict()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            StorageKey = "PENDING:upload-123",
            UploadedAt = DateTime.UtcNow,
            FileSize = 0
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FileInfo(id);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task FileInfo_ManagedFileFailed_Returns422()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            StorageKey = "FAILED:network-error",
            UploadedAt = DateTime.UtcNow,
            FileSize = 0
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FileInfo(id);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task FileInfo_DocumentMetadataPending_Returns409()
    {
        var id = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = id,
            FileName = "doc.pdf",
            StorageKey = "PENDING:upload-123",
            UploadedAt = DateTime.UtcNow,
            FileSize = 0
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.FileInfo(id);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(409));
    }

    // ── DownloadAbbreviationsCsv ─────────────────────────────────────────────

    [Test]
    public async Task DownloadAbbreviationsCsv_EmptyDb_ReturnsCsvWithHeaderOnly()
    {
        var controller = CreateController();
        var result = await controller.DownloadAbbreviationsCsv();

        Assert.That(result, Is.InstanceOf<FileContentResult>());
        var file = (FileContentResult)result;
        Assert.That(file.ContentType, Does.Contain("text/csv"));
        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.That(csv, Does.Contain("ShortForm"));
        Assert.That(csv, Does.Contain("ExpandedForm"));
    }

    [Test]
    public async Task DownloadAbbreviationsCsv_WithAbbreviations_IncludesRows()
    {
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "BP",
            ExpandedForm = "blood pressure",
            Source = "FDA",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "HR",
            ExpandedForm = "heart rate",
            Source = "FDA",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.DownloadAbbreviationsCsv();

        var file = (FileContentResult)result;
        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.That(csv, Does.Contain("BP"));
        Assert.That(csv, Does.Contain("blood pressure"));
        Assert.That(csv, Does.Contain("HR"));
    }

    // ── DeleteFile ───────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteFile_NotFound_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.DeleteFile(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task DeleteFile_FileExists_Returns204NoContent()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "deleteme.pdf",
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/deleteme.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 512
        });
        await _context.SaveChangesAsync();

        _mediaStorageMock.Setup(s => s.DeleteMediaAsync("s3://bucket/deleteme.pdf"))
            .Returns(Task.CompletedTask);

        var controller = CreateController();
        var result = await controller.DeleteFile(id);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(await _context.ManagedFiles.FindAsync(id), Is.Null);
    }

    [Test]
    public async Task DeleteFile_StorageDeleteThrows_Returns500()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "deleteme.pdf",
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/deleteme.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 512
        });
        await _context.SaveChangesAsync();

        _mediaStorageMock.Setup(s => s.DeleteMediaAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("S3 unavailable"));

        var controller = CreateController();
        var result = await controller.DeleteFile(id);

        var status = result as ObjectResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }

    // ── DownloadFile ─────────────────────────────────────────────────────────

    [Test]
    public async Task DownloadFile_NeitherManagedFileNorDoc_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.DownloadFile(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task DownloadFile_ManagedFileExists_StorageFails_Returns500()
    {
        var id = Guid.NewGuid();
        _context.ManagedFiles.Add(new ManagedFile
        {
            Id = id,
            FileName = "report.pdf",
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024
        });
        await _context.SaveChangesAsync();

        _mediaStorageMock.Setup(s => s.DownloadFileAsync("s3://bucket/report.pdf"))
            .ThrowsAsync(new Exception("S3 error"));

        var controller = CreateController();
        var result = await controller.DownloadFile(id);

        // DownloadFile catches exceptions and returns StatusCode(500) (no body)
        var status = result as StatusCodeResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task DownloadFile_DocumentMetadataExists_StorageFails_Returns500()
    {
        var id = Guid.NewGuid();
        _context.DocumentMetadatas.Add(new DocumentMetadata
        {
            Id = id,
            FileName = "report.pdf",
            OriginalFileName = "original-report.pdf",
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/report.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024
        });
        await _context.SaveChangesAsync();

        _mediaStorageMock.Setup(s => s.DownloadFileAsync("s3://bucket/report.pdf"))
            .ThrowsAsync(new Exception("S3 error"));

        var controller = CreateController();
        var result = await controller.DownloadFile(id);

        var status = result as StatusCodeResult;
        Assert.That(status?.StatusCode, Is.EqualTo(500));
    }
}
