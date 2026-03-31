using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class DocumentSearchTaskHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DocumentSearchTaskHandler CreateHandler(ApplicationDbContext context)
    {
        var logger = new Mock<ILogger<DocumentSearchTaskHandler>>().Object;
        return new DocumentSearchTaskHandler(context, logger);
    }

    private static async Task<(DocumentMetadata meta, DocumentChunk chunk)> AddChunkAsync(
        ApplicationDbContext context,
        string fileName,
        string chunkText,
        int pageNumber = 1)
    {
        var meta = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            OriginalFileName = fileName,
            ContentType = "application/pdf",
            StorageKey = Guid.NewGuid().ToString(),
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024,
            DocumentType = "pdf"
        };
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = meta.Id,
            DocumentMetadata = meta,
            ChunkText = chunkText,
            PageNumber = pageNumber,
            ChunkIndex = 0,
            CreatedAt = DateTime.UtcNow
        };
        context.DocumentMetadatas.Add(meta);
        context.DocumentChunks.Add(chunk);
        await context.SaveChangesAsync();
        return (meta, chunk);
    }

    // ── Metadata ────────────────────────────────────────────────────────────

    [Test]
    public void Name_ShouldBeDocumentSearch()
    {
        using var context = CreateContext();
        var handler = CreateHandler(context);

        Assert.That(handler.Name, Is.EqualTo("document-search"));
    }

    [Test]
    public void Keywords_ShouldContainSearchFor()
    {
        using var context = CreateContext();
        var handler = CreateHandler(context);

        Assert.That(handler.Keywords, Contains.Item("search for"));
    }

    // ── Empty / not-found cases ─────────────────────────────────────────────

    [Test]
    public async Task HandleAsync_EmptySearchTerm_ReturnsNoTermError()
    {
        using var context = CreateContext();
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(string.Empty);

        Assert.That(result, Does.Contain("No search term"));
    }

    [Test]
    public async Task HandleAsync_TermNotFound_ReturnsNotFoundMessage()
    {
        using var context = CreateContext();
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync("xyz_not_in_db");

        Assert.That(result, Does.Contain("was not found"));
    }

    // ── Basic match ─────────────────────────────────────────────────────────

    [Test]
    public async Task HandleAsync_TermFound_ReturnsMatchWithDocumentName()
    {
        using var context = CreateContext();
        await AddChunkAsync(context, "PatientGuide.pdf", "patient information section");
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync("search for patient information");

        Assert.That(result, Does.Contain("patient information").IgnoreCase);
        Assert.That(result, Does.Contain("PatientGuide.pdf"));
    }

    // ── Prefix stripping ────────────────────────────────────────────────────

    [Test]
    public async Task HandleAsync_WithSearchForPrefix_ExtractsSearchTerm()
    {
        using var context = CreateContext();
        await AddChunkAsync(context, "CardioReport.pdf", "blood pressure monitoring results");
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync("search for blood pressure");

        Assert.That(result, Does.Contain("blood pressure").IgnoreCase);
    }

    [Test]
    public async Task HandleAsync_WithFindPrefix_ExtractsSearchTerm()
    {
        using var context = CreateContext();
        await AddChunkAsync(context, "ClinicalNotes.pdf", "patient assessment completed");
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync("find patient");

        Assert.That(result, Does.Contain("patient").IgnoreCase);
    }

    [Test]
    public async Task HandleAsync_WithLocatePrefix_ExtractsSearchTerm()
    {
        using var context = CreateContext();
        await AddChunkAsync(context, "SecurityPolicy.pdf", "security guidelines and procedures");
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync("locate the security");

        Assert.That(result, Does.Contain("security").IgnoreCase);
    }

    // ── Abbreviation expansion ──────────────────────────────────────────────

    [Test]
    public async Task HandleAsync_AbbreviationExpansion_SearchesBothForms()
    {
        using var context = CreateContext();
        // Add a chunk containing the expanded form; searching for abbreviation "BP" should find it
        await AddChunkAsync(context, "DrugLabel.pdf", "blood pressure must be monitored carefully");
        var handler = CreateHandler(context);

        // "BP" expands to "blood pressure" in the built-in FDA SPL map
        var result = await handler.HandleAsync("search for BP");

        Assert.That(result, Does.Contain("blood pressure").IgnoreCase
            .Or.Contain("DrugLabel.pdf"));
    }

    // ── Document filter ─────────────────────────────────────────────────────

    [Test]
    public async Task HandleAsync_DocumentFilter_FiltersResults()
    {
        using var context = CreateContext();
        await AddChunkAsync(context, "TestDoc.pdf", "patient history notes");
        await AddChunkAsync(context, "OtherDoc.pdf", "patient history records");
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync("find patient in document TestDoc.pdf");

        Assert.That(result, Does.Contain("TestDoc.pdf"));
        Assert.That(result, Does.Not.Contain("OtherDoc.pdf"));
    }

    // ── Multiple matches ────────────────────────────────────────────────────

    [Test]
    public async Task HandleAsync_MultipleMatches_ReturnsAll()
    {
        using var context = CreateContext();
        await AddChunkAsync(context, "Doc1.pdf", "hypertension management overview", 1);
        await AddChunkAsync(context, "Doc2.pdf", "hypertension drug dosage", 2);
        await AddChunkAsync(context, "Doc3.pdf", "hypertension clinical trial results", 3);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync("search for hypertension");

        Assert.That(result, Does.Contain("Doc1.pdf"));
        Assert.That(result, Does.Contain("Doc2.pdf"));
        Assert.That(result, Does.Contain("Doc3.pdf"));
    }
}
