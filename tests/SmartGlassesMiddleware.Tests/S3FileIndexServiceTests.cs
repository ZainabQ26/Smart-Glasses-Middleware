using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class S3FileIndexServiceTests
{
    private ApplicationDbContext _db = null!;
    private Mock<IMediaStorageService> _storageMock = null!;
    private S3FileIndexService _service = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _storageMock = new Mock<IMediaStorageService>();

        // Build a minimal service provider that resolves IMediaStorageService and ApplicationDbContext
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton(_storageMock.Object);
        var provider = services.BuildServiceProvider();

        _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = Mock.Of<ILogger<S3FileIndexService>>();
        _service = new S3FileIndexService(_scopeFactory, logger);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    // ── BuildUploadMetadata ───────────────────────────────────────────────────

    [Test]
    public void BuildUploadMetadata_ContainsExpectedKeys()
    {
        var id = Guid.NewGuid();
        var meta = S3FileIndexService.BuildUploadMetadata(id, "report.pdf", "Annual Report");

        Assert.That(meta.ContainsKey(S3FileIndexService.MetaFileId), Is.True);
        Assert.That(meta.ContainsKey(S3FileIndexService.MetaFileName), Is.True);
        Assert.That(meta.ContainsKey(S3FileIndexService.MetaLabel), Is.True);
        Assert.That(meta[S3FileIndexService.MetaFileId], Is.EqualTo(id.ToString()));
        Assert.That(meta[S3FileIndexService.MetaFileName], Is.EqualTo("report.pdf"));
        Assert.That(meta[S3FileIndexService.MetaLabel], Is.EqualTo("Annual Report"));
    }

    // ── StartAsync — happy path ───────────────────────────────────────────────

    [Test]
    public async Task StartAsync_NewFiles_AreIndexedInDb()
    {
        var fileId = Guid.NewGuid();
        var s3Files = new List<StoredFileInfo>
        {
            new StoredFileInfo(
                Key: $"{fileId}/report.pdf",
                FileName: "report.pdf",
                ContentType: "application/pdf",
                Size: 12345,
                LastModified: DateTime.UtcNow.AddDays(-1),
                UserMetadata: new Dictionary<string, string>
                {
                    [S3FileIndexService.MetaFileId] = fileId.ToString(),
                    [S3FileIndexService.MetaFileName] = "report.pdf",
                    [S3FileIndexService.MetaLabel] = "Annual Report"
                })
        };

        _storageMock.Setup(s => s.ListFilesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(s3Files);

        await _service.StartAsync(CancellationToken.None);
        await _service.StopAsync(CancellationToken.None);

        var indexed = await _db.ManagedFiles.ToListAsync();
        Assert.That(indexed, Has.Count.EqualTo(1));
        Assert.That(indexed[0].Id, Is.EqualTo(fileId));
        Assert.That(indexed[0].FileName, Is.EqualTo("report.pdf"));
        Assert.That(indexed[0].Label, Is.EqualTo("Annual Report"));
        Assert.That(indexed[0].StorageKey, Is.EqualTo($"{fileId}/report.pdf"));
        Assert.That(indexed[0].FileSize, Is.EqualTo(12345));
    }

    [Test]
    public async Task StartAsync_FileAlreadyInDb_IsNotDuplicated()
    {
        var fileId = Guid.NewGuid();
        var key = $"{fileId}/report.pdf";

        // Pre-seed the DB
        _db.ManagedFiles.Add(new ManagedFile
        {
            Id = fileId,
            FileName = "report.pdf",
            Label = "Annual Report",
            ContentType = "application/pdf",
            StorageKey = key,
            UploadedAt = DateTime.UtcNow,
            FileSize = 100
        });
        await _db.SaveChangesAsync();

        var s3Files = new List<StoredFileInfo>
        {
            new StoredFileInfo(
                Key: key,
                FileName: "report.pdf",
                ContentType: "application/pdf",
                Size: 100,
                LastModified: DateTime.UtcNow,
                UserMetadata: new Dictionary<string, string>
                {
                    [S3FileIndexService.MetaFileId] = fileId.ToString()
                })
        };

        _storageMock.Setup(s => s.ListFilesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(s3Files);

        await _service.StartAsync(CancellationToken.None);
        await _service.StopAsync(CancellationToken.None);

        var indexed = await _db.ManagedFiles.ToListAsync();
        Assert.That(indexed, Has.Count.EqualTo(1), "Should not duplicate existing records");
    }

    [Test]
    public async Task StartAsync_SystemKeysSkipped()
    {
        var s3Files = new List<StoredFileInfo>
        {
            new StoredFileInfo(
                Key: "_state/snapshot.json",
                FileName: "snapshot.json",
                ContentType: "application/json",
                Size: 500,
                LastModified: DateTime.UtcNow,
                UserMetadata: new Dictionary<string, string>())
        };

        _storageMock.Setup(s => s.ListFilesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(s3Files);

        await _service.StartAsync(CancellationToken.None);
        await _service.StopAsync(CancellationToken.None);

        var indexed = await _db.ManagedFiles.ToListAsync();
        Assert.That(indexed, Is.Empty, "_state/ prefix should be skipped");
    }

    [Test]
    public async Task StartAsync_NoMetadata_InfersFileNameFromKey()
    {
        var fileId = Guid.NewGuid();
        var key = $"{fileId}/document.jpg";

        var s3Files = new List<StoredFileInfo>
        {
            new StoredFileInfo(
                Key: key,
                FileName: "document.jpg",     // MinIO/SDK infers this from key
                ContentType: "image/jpeg",
                Size: 2048,
                LastModified: DateTime.UtcNow,
                UserMetadata: new Dictionary<string, string>()) // No sgm-* metadata
        };

        _storageMock.Setup(s => s.ListFilesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(s3Files);

        await _service.StartAsync(CancellationToken.None);
        await _service.StopAsync(CancellationToken.None);

        var indexed = await _db.ManagedFiles.ToListAsync();
        Assert.That(indexed, Has.Count.EqualTo(1));
        Assert.That(indexed[0].FileName, Is.EqualTo("document.jpg"));
        // ID inferred from first path segment (Guid)
        Assert.That(indexed[0].Id, Is.EqualTo(fileId));
    }

    [Test]
    public async Task StartAsync_S3ListThrows_DoesNotCrash()
    {
        _storageMock.Setup(s => s.ListFilesAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new Exception("S3 not configured"));

        // Should complete without throwing
        Assert.DoesNotThrowAsync(() => _service.StartAsync(CancellationToken.None));
        Assert.DoesNotThrowAsync(() => _service.StopAsync(CancellationToken.None));
    }

    [Test]
    public async Task StartAsync_MultipleFiles_AllIndexed()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var s3Files = new List<StoredFileInfo>
        {
            new StoredFileInfo(
                Key: $"{id1}/file1.pdf",
                FileName: "file1.pdf",
                ContentType: "application/pdf",
                Size: 111,
                LastModified: DateTime.UtcNow,
                UserMetadata: new Dictionary<string, string>
                {
                    [S3FileIndexService.MetaFileId] = id1.ToString(),
                    [S3FileIndexService.MetaFileName] = "file1.pdf",
                    [S3FileIndexService.MetaLabel] = "File One"
                }),
            new StoredFileInfo(
                Key: $"{id2}/file2.png",
                FileName: "file2.png",
                ContentType: "image/png",
                Size: 222,
                LastModified: DateTime.UtcNow,
                UserMetadata: new Dictionary<string, string>
                {
                    [S3FileIndexService.MetaFileId] = id2.ToString(),
                    [S3FileIndexService.MetaFileName] = "file2.png",
                    [S3FileIndexService.MetaLabel] = "File Two"
                })
        };

        _storageMock.Setup(s => s.ListFilesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(s3Files);

        await _service.StartAsync(CancellationToken.None);
        await _service.StopAsync(CancellationToken.None);

        var indexed = await _db.ManagedFiles.ToListAsync();
        Assert.That(indexed, Has.Count.EqualTo(2));
    }

    [Test]
    public void StopAsync_CompletesWithoutError()
    {
        Assert.DoesNotThrowAsync(() => _service.StopAsync(CancellationToken.None));
    }
}
