using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class MoreRAGServiceTests
{
    private ApplicationDbContext _context = null!;
    private Mock<IAIService> _aiServiceMock = null!;
    private Kernel _kernel;
    private Mock<IServiceProvider> _serviceProviderMock = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _aiServiceMock = new Mock<IAIService>();
        _kernel = Kernel.CreateBuilder().Build();
        _serviceProviderMock = new Mock<IServiceProvider>();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private RAGService CreateService(bool useVectorDb = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG:Enabled"] = "true",
                ["RAG:MaxContextTokens"] = "4000",
                ["RAG:UseVectorDB"] = useVectorDb.ToString().ToLower(),
            })
            .Build();

        return new RAGService(
            _context,
            Mock.Of<ILogger<RAGService>>(),
            _aiServiceMock.Object,
            _kernel,
            _serviceProviderMock.Object,
            config);
    }

    [Test]
    public async Task SearchChunksAsync_EmptyDb_ReturnsEmptyList()
    {
        var service = CreateService();

        var result = await service.SearchChunksAsync("test query");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task SearchChunksAsync_WithMatchingChunks_ReturnsChunks()
    {
        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "medicine.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/medicine.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = true
        };
        _context.DocumentMetadatas.Add(doc);

        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = doc.Id,
            ChunkText = "aspirin is used for pain relief",
            ChunkIndex = 0,
            Embedding = null
        };
        _context.DocumentChunks.Add(chunk);
        await _context.SaveChangesAsync();

        var service = CreateService();
        var result = await service.SearchChunksAsync("aspirin pain");

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ReprocessFailedDocumentsAsync_NoFailedDocs_ReturnsZero()
    {
        var service = CreateService();

        var result = await service.ReprocessFailedDocumentsAsync();

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task ReprocessFailedDocumentsAsync_WithFailedDocs_DoesNotThrow()
    {
        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "failed.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/failed.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = false,
            ProcessingError = "previous error"
        };
        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        var service = CreateService();

        Assert.DoesNotThrowAsync(async () => await service.ReprocessFailedDocumentsAsync());
    }

    [Test]
    public async Task ReindexAllDocumentsAsync_EmptyDb_ReturnsZero()
    {
        var service = CreateService();

        var result = await service.ReindexAllDocumentsAsync(force: false);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task GetUnprocessedDocumentsAsync_WithProcessedDoc_ReturnsEmpty()
    {
        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "done.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/done.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = true,
            ProcessingError = null
        };
        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        var service = CreateService();
        var result = await service.GetUnprocessedDocumentsAsync();

        Assert.That(result, Is.Empty);
    }
}
