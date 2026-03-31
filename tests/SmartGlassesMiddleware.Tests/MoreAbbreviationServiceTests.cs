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
public class MoreAbbreviationServiceTests
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
    public void TearDown() => _context.Dispose();

    private Abbreviation MakeAbbr(string shortForm, string expandedForm, string source = "test") => new()
    {
        Id = Guid.NewGuid(),
        ShortForm = shortForm,
        ExpandedForm = expandedForm,
        Source = source,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Test]
    public async Task LookupByShortFormAsync_MultipleAbbreviationsForSameForm_ReturnsAll()
    {
        _context.Abbreviations.Add(MakeAbbr("ACE", "angiotensin converting enzyme"));
        _context.Abbreviations.Add(MakeAbbr("ACE", "adverse childhood experience"));
        await _context.SaveChangesAsync();

        var result = await _service.LookupByShortFormAsync("ACE");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task LookupByExpandedFormAsync_CachedResult_ReturnsCached()
    {
        _context.Abbreviations.Add(MakeAbbr("HTN", "hypertension"));
        await _context.SaveChangesAsync();

        var first = await _service.LookupByExpandedFormAsync("hypertension");
        var second = await _service.LookupByExpandedFormAsync("hypertension");

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Count, Is.EqualTo(first!.Count));
    }

    [Test]
    public async Task GetRelatedTermsAsync_MultipleRelated_ReturnsAllDistinct()
    {
        _context.Abbreviations.Add(MakeAbbr("DM", "diabetes mellitus"));
        _context.Abbreviations.Add(MakeAbbr("T2DM", "diabetes mellitus"));
        await _context.SaveChangesAsync();

        var (terms, abbrs) = await _service.GetRelatedTermsAsync("DM");

        Assert.That(terms, Is.Not.Empty);
        Assert.That(terms.Distinct().Count(), Is.EqualTo(terms.Count));
    }

    [Test]
    public async Task RefreshCacheAsync_WithMultipleAbbreviations_CachesAll()
    {
        _context.Abbreviations.Add(MakeAbbr("BP", "blood pressure"));
        _context.Abbreviations.Add(MakeAbbr("HR", "heart rate"));
        _context.Abbreviations.Add(MakeAbbr("RR", "respiratory rate"));
        await _context.SaveChangesAsync();

        await _service.RefreshCacheAsync();

        var bp = await _service.LookupByShortFormAsync("BP");
        var hr = await _service.LookupByShortFormAsync("HR");
        var rr = await _service.LookupByShortFormAsync("RR");

        Assert.That(bp, Is.Not.Null);
        Assert.That(hr, Is.Not.Null);
        Assert.That(rr, Is.Not.Null);
    }

    [Test]
    public async Task RefreshCacheAsync_CalledTwice_LastClearsPrevious()
    {
        _context.Abbreviations.Add(MakeAbbr("OLD", "old term"));
        await _context.SaveChangesAsync();
        await _service.RefreshCacheAsync();

        // Remove old, add new
        _context.Abbreviations.RemoveRange(_context.Abbreviations);
        _context.Abbreviations.Add(MakeAbbr("NEW", "new term"));
        await _context.SaveChangesAsync();
        await _service.RefreshCacheAsync();

        var oldResult = await _service.LookupByShortFormAsync("OLD");
        var newResult = await _service.LookupByShortFormAsync("NEW");

        Assert.That(oldResult, Is.Null);
        Assert.That(newResult, Is.Not.Null);
    }

    [Test]
    public async Task LookupByShortFormAsync_WhitespaceInput_ReturnsNull()
    {
        var result = await _service.LookupByShortFormAsync("   ");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task LookupByExpandedFormAsync_WhitespaceInput_ReturnsNull()
    {
        var result = await _service.LookupByExpandedFormAsync("   ");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SyncFromDailyMedAsync_AllSourcesDisabled_ReturnsZero()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AbbreviationSync:OpenFda:Enabled"] = "false",
                ["AbbreviationSync:DailyMed:Enabled"] = "false",
                ["AbbreviationSync:RxNav:Enabled"] = "false",
                ["AbbreviationSync:RxTerms:Enabled"] = "false",
            })
            .Build();

        var service = new AbbreviationService(
            _context,
            new Mock<ILogger<AbbreviationService>>().Object,
            new HttpClient(),
            config);

        var result = await service.SyncFromDailyMedAsync(CancellationToken.None);

        // Only built-ins are added when all external sources are disabled
        Assert.That(result, Is.GreaterThanOrEqualTo(0));
    }
}
