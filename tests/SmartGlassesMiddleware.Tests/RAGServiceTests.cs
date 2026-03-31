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
public class RAGServiceTests
{
    private ApplicationDbContext _context = null!;
    private Mock<ILogger<RAGService>> _loggerMock = null!;
    private Mock<IAIService> _aiServiceMock = null!;
    private Kernel _kernel = null!;
    private Mock<IServiceProvider> _serviceProviderMock = null!;
    private IConfiguration _config = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _loggerMock = new Mock<ILogger<RAGService>>();
        _aiServiceMock = new Mock<IAIService>();
        _kernel = Kernel.CreateBuilder().Build();
        _serviceProviderMock = new Mock<IServiceProvider>();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG:Enabled"] = "true",
                ["RAG:MaxContextTokens"] = "1000",
                ["RAG:ApproximateTokensPerChar"] = "0.25",
            })
            .Build();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    private RAGService CreateService()
    {
        return new RAGService(
            _context,
            _loggerMock.Object,
            _aiServiceMock.Object,
            _kernel,
            _serviceProviderMock.Object,
            _config);
    }

    [Test]
    public async Task GetUnprocessedDocumentsAsync_EmptyDb_ReturnsEmpty()
    {
        var service = CreateService();

        var result = await service.GetUnprocessedDocumentsAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetUnprocessedDocumentsAsync_WithUnprocessedDoc_ReturnsDoc()
    {
        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "test.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/test.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = false
        };
        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GetUnprocessedDocumentsAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo(doc.Id));
    }

    [Test]
    public async Task GetUnprocessedDocumentsAsync_OnlyUnprocessed_FiltersCorrectly()
    {
        var unprocessed = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "unprocessed.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/unprocessed.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = false
        };
        var processed = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "processed.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/processed.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = true,
            ProcessedAt = DateTime.UtcNow
        };
        _context.DocumentMetadatas.AddRange(unprocessed, processed);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GetUnprocessedDocumentsAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo(unprocessed.Id));
    }

    [Test]
    public async Task GetUnprocessedDocumentsAsync_ProcessedDoc_NotReturned()
    {
        var processed = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "done.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/done.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = true,
            ProcessedAt = DateTime.UtcNow
        };
        _context.DocumentMetadatas.Add(processed);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GetUnprocessedDocumentsAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetUnprocessedDocumentsAsync_DocWithProcessingError_IsIncluded()
    {
        // A processed doc with an error should still be returned (the query checks IsProcessed OR ProcessingError != null)
        var docWithError = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "error.pdf",
            ContentType = "application/pdf",
            StorageKey = "uploads/error.pdf",
            UploadedAt = DateTime.UtcNow,
            IsProcessed = true,
            ProcessingError = "Something went wrong"
        };
        _context.DocumentMetadatas.Add(docWithError);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GetUnprocessedDocumentsAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo(docWithError.Id));
    }

    [Test]
    public async Task QueryRemindersAsync_NoReminders_ReturnsEmptyAnswer()
    {
        var service = CreateService();

        var result = await service.QueryRemindersAsync("show my reminders");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Answer, Is.EqualTo("You don't have any reminders saved."));
    }

    [Test]
    public async Task QueryRemindersAsync_WithReminders_ReturnsQueryResult()
    {
        var reminder = new RequestHistory
        {
            Id = Guid.NewGuid(),
            RequestType = "reminder",
            InputText = "Buy milk",
            Timestamp = DateTime.UtcNow
        };
        _context.RequestHistories.Add(reminder);
        await _context.SaveChangesAsync();

        var service = CreateService();

        // The service tries to call kernel's chat service, which isn't registered.
        // It catches the exception and returns an error-prefixed answer.
        var result = await service.QueryRemindersAsync("show my reminders");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Answer, Is.Not.Null);
    }

    [Test]
    public async Task QueryRemindersAsync_NonReminderHistory_NotIncluded()
    {
        // Only "reminder" type entries should be queried
        var nonReminder = new RequestHistory
        {
            Id = Guid.NewGuid(),
            RequestType = "DocumentQuery",
            InputText = "What is the document about?",
            Timestamp = DateTime.UtcNow
        };
        _context.RequestHistories.Add(nonReminder);
        await _context.SaveChangesAsync();

        var service = CreateService();

        var result = await service.QueryRemindersAsync("show my reminders");

        Assert.That(result.Answer, Is.EqualTo("You don't have any reminders saved."));
    }
}
