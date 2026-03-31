using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class AbbreviationServiceTests
{
    private ApplicationDbContext _context = null!;
    private AbbreviationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        var logger = new Mock<ILogger<AbbreviationService>>().Object;
        var httpClient = new HttpClient();
        var config = new ConfigurationBuilder().Build();
        _service = new AbbreviationService(_context, logger, httpClient, config);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    // ── LookupByShortFormAsync ──────────────────────────────────────────────

    [Test]
    public async Task LookupByShortFormAsync_NullOrEmpty_ReturnsNull()
    {
        Assert.That(await _service.LookupByShortFormAsync(null!), Is.Null);
        Assert.That(await _service.LookupByShortFormAsync(string.Empty), Is.Null);
    }

    [Test]
    public async Task LookupByShortFormAsync_ExistingInDb_ReturnsResult()
    {
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "ACE",
            ExpandedForm = "angiotensin converting enzyme",
            Source = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _service.LookupByShortFormAsync("ACE");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result![0].ExpandedForm, Is.EqualTo("angiotensin converting enzyme"));
    }

    [Test]
    public async Task LookupByShortFormAsync_NotInDb_ChecksBuiltIn()
    {
        // "QD" is a built-in abbreviation (once daily) and is not in the in-memory DB
        var result = await _service.LookupByShortFormAsync("QD");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.GreaterThan(0));
        Assert.That(result![0].ExpandedForm, Is.EqualTo("once daily"));
    }

    [Test]
    public async Task LookupByShortFormAsync_CachesResult()
    {
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "BID",
            ExpandedForm = "twice daily",
            Source = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // First call populates the cache
        var first = await _service.LookupByShortFormAsync("BID");

        // Remove from DB so any cache miss would return null from DB path
        _context.Abbreviations.RemoveRange(_context.Abbreviations);
        await _context.SaveChangesAsync();

        // Second call should still return the cached value
        var second = await _service.LookupByShortFormAsync("BID");

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(second![0].ExpandedForm, Is.EqualTo(first![0].ExpandedForm));
    }

    // ── LookupByExpandedFormAsync ───────────────────────────────────────────

    [Test]
    public async Task LookupByExpandedFormAsync_NullOrEmpty_ReturnsNull()
    {
        Assert.That(await _service.LookupByExpandedFormAsync(null!), Is.Null);
        Assert.That(await _service.LookupByExpandedFormAsync(string.Empty), Is.Null);
    }

    [Test]
    public async Task LookupByExpandedFormAsync_ExistingInDb_ReturnsResult()
    {
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "TID",
            ExpandedForm = "three times daily",
            Source = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _service.LookupByExpandedFormAsync("three times daily");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result![0].ShortForm, Is.EqualTo("TID"));
    }

    [Test]
    public async Task LookupByExpandedFormAsync_NotInDb_UsesBuiltIn()
    {
        // "once daily" is the expanded form for built-in "QD"
        var result = await _service.LookupByExpandedFormAsync("once daily");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.GreaterThan(0));
        Assert.That(result!.Any(a => a.ShortForm == "QD"), Is.True);
    }

    // ── GetRelatedTermsAsync ────────────────────────────────────────────────

    [Test]
    public async Task GetRelatedTermsAsync_EmptyInput_ReturnsEmptyLists()
    {
        var (terms, abbreviations) = await _service.GetRelatedTermsAsync(string.Empty);

        Assert.That(terms, Is.Empty);
        Assert.That(abbreviations, Is.Empty);
    }

    [Test]
    public async Task GetRelatedTermsAsync_WithShortForm_IncludesExpandedForm()
    {
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "ACE",
            ExpandedForm = "angiotensin converting enzyme",
            Source = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var (terms, abbreviations) = await _service.GetRelatedTermsAsync("ACE");

        Assert.That(terms, Contains.Item("ACE"));
        Assert.That(terms, Contains.Item("angiotensin converting enzyme"));
        Assert.That(abbreviations, Has.Count.GreaterThan(0));
    }

    [Test]
    public async Task GetRelatedTermsAsync_WithExpandedForm_IncludesShortForm()
    {
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "IV",
            ExpandedForm = "intravenous",
            Source = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var (terms, abbreviations) = await _service.GetRelatedTermsAsync("intravenous");

        Assert.That(terms, Contains.Item("intravenous"));
        Assert.That(terms, Contains.Item("IV"));
        Assert.That(abbreviations, Has.Count.GreaterThan(0));
    }

    [Test]
    public async Task GetRelatedTermsAsync_BuiltInAbbreviation_ReturnsRelated()
    {
        // "QD" is a built-in abbreviation; its expanded form "once daily" should appear
        var (terms, abbreviations) = await _service.GetRelatedTermsAsync("QD");

        Assert.That(terms, Contains.Item("QD"));
        Assert.That(terms, Contains.Item("once daily"));
    }

    [Test]
    public async Task GetRelatedTermsAsync_NoMatch_ReturnsOnlyOriginal()
    {
        var (terms, abbreviations) = await _service.GetRelatedTermsAsync("XYZ123NOTFOUND");

        Assert.That(terms, Has.Count.EqualTo(1));
        Assert.That(terms[0], Is.EqualTo("XYZ123NOTFOUND"));
        Assert.That(abbreviations, Is.Empty);
    }

    // ── RefreshCacheAsync ───────────────────────────────────────────────────

    [Test]
    public async Task RefreshCacheAsync_WithAbbreviationsInDb_PopulatesCache()
    {
        _context.Abbreviations.Add(new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "QID",
            ExpandedForm = "four times daily",
            Source = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        await _service.RefreshCacheAsync();

        // Remove from DB; result must now come from cache
        _context.Abbreviations.RemoveRange(_context.Abbreviations);
        await _context.SaveChangesAsync();

        var result = await _service.LookupByShortFormAsync("QID");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result![0].ExpandedForm, Is.EqualTo("four times daily"));
    }

    [Test]
    public async Task RefreshCacheAsync_EmptyDb_SeedsBuiltIns()
    {
        // Empty DB — RefreshCacheAsync clears cache, loads nothing from DB.
        // Built-in fallback in LookupByShortFormAsync should still work.
        await _service.RefreshCacheAsync();

        var result = await _service.LookupByShortFormAsync("BID");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.GreaterThan(0));
        Assert.That(result![0].ExpandedForm, Is.EqualTo("twice daily"));
    }

    // ── Case insensitivity ──────────────────────────────────────────────────

    [Test]
    public async Task LookupByShortFormAsync_CaseSensitivity_IgnoresCase()
    {
        // "qd" (lowercase) should match built-in "QD"
        var result = await _service.LookupByShortFormAsync("qd");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.GreaterThan(0));
        Assert.That(result![0].ExpandedForm, Is.EqualTo("once daily"));
    }
}
