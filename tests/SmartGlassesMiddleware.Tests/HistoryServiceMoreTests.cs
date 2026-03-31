using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class HistoryServiceMoreTests
{
    private ApplicationDbContext _context = null!;
    private HistoryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _service = new HistoryService(_context, Mock.Of<ILogger<HistoryService>>());
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private async Task<RequestHistory> AddReminder(string text = "Take aspirin")
    {
        var entry = new RequestHistory
        {
            Id = Guid.NewGuid(),
            RequestType = "reminder",
            InputText = text,
            Timestamp = DateTime.UtcNow
        };
        _context.RequestHistories.Add(entry);
        await _context.SaveChangesAsync();
        return entry;
    }

    private async Task<RequestHistory> AddNonReminder()
    {
        var entry = new RequestHistory
        {
            Id = Guid.NewGuid(),
            RequestType = "query",
            InputText = "What is the weather?",
            Timestamp = DateTime.UtcNow
        };
        _context.RequestHistories.Add(entry);
        await _context.SaveChangesAsync();
        return entry;
    }

    [Test]
    public async Task GetHistoryByTypeAsync_EmptyRequestType_ReturnsEmpty()
    {
        await AddReminder();

        var result = await _service.GetHistoryByTypeAsync(string.Empty);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetHistoryByTypeAsync_WhitespaceRequestType_ReturnsEmpty()
    {
        await AddReminder();

        var result = await _service.GetHistoryByTypeAsync("   ");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void UpdateReminderAsync_EmptyText_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.UpdateReminderAsync(Guid.NewGuid(), string.Empty));
    }

    [Test]
    public void UpdateReminderAsync_WhitespaceText_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.UpdateReminderAsync(Guid.NewGuid(), "   "));
    }

    [Test]
    public void UpdateReminderAsync_TextTooLong_ThrowsArgumentException()
    {
        var longText = new string('x', 1001);
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.UpdateReminderAsync(Guid.NewGuid(), longText));
    }

    [Test]
    public async Task UpdateReminderAsync_NonReminderEntry_ThrowsInvalidOperationException()
    {
        var entry = await AddNonReminder();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.UpdateReminderAsync(entry.Id, "Updated text"));
    }

    [Test]
    public async Task UpdateReminderAsync_ValidReminder_UpdatesSuccessfully()
    {
        var entry = await AddReminder("Original text");

        var result = await _service.UpdateReminderAsync(entry.Id, "Updated text");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.InputText, Is.EqualTo("Updated text"));
    }

    [Test]
    public async Task DeleteReminderAsync_NonReminderEntry_ThrowsInvalidOperationException()
    {
        var entry = await AddNonReminder();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.DeleteReminderAsync(entry.Id));
    }

    [Test]
    public async Task DeleteReminderAsync_NotFound_ReturnsFalse()
    {
        var result = await _service.DeleteReminderAsync(Guid.NewGuid());
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteReminderAsync_ValidReminder_ReturnsTrue()
    {
        var entry = await AddReminder();

        var result = await _service.DeleteReminderAsync(entry.Id);

        Assert.That(result, Is.True);
        Assert.That(await _context.RequestHistories.FindAsync(entry.Id), Is.Null);
    }
}
