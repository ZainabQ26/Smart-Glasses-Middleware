using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class InMemoryCacheServiceMoreTests
{
    private InMemoryCacheService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new InMemoryCacheService(new Mock<ILogger<InMemoryCacheService>>().Object);
    }

    [Test]
    public async Task GetAsync_KeyDoesNotExist_ReturnsDefault()
    {
        var result = await _service.GetAsync<string>("missing-key");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SetAsync_NullExpiry_ItemPersists()
    {
        await _service.SetAsync("persist-key", "value", expiry: null);
        var result = await _service.GetAsync<string>("persist-key");
        Assert.That(result, Is.EqualTo("value"));
    }

    [Test]
    public async Task SetAsync_OverwritesExistingKey()
    {
        await _service.SetAsync("key", "first");
        await _service.SetAsync("key", "second");
        var result = await _service.GetAsync<string>("key");
        Assert.That(result, Is.EqualTo("second"));
    }

    [Test]
    public async Task RemoveAsync_ExistingKey_ItemGone()
    {
        await _service.SetAsync("remove-me", 42);
        await _service.RemoveAsync("remove-me");
        var result = await _service.GetAsync<int?>("remove-me");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        Assert.DoesNotThrowAsync(async () => await _service.RemoveAsync("nope"));
    }

    [Test]
    public async Task GetAsync_ExpiredItem_ReturnsDefault()
    {
        await _service.SetAsync("expiring", "val", expiry: TimeSpan.FromMilliseconds(1));
        await Task.Delay(20);
        var result = await _service.GetAsync<string>("expiring");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SetAsync_ComplexObject_RoundTrips()
    {
        var obj = new { Name = "test", Value = 123 };
        await _service.SetAsync("obj-key", obj);
        var result = await _service.GetAsync<System.Text.Json.JsonElement>("obj-key");
        Assert.That(result.GetProperty("Name").GetString(), Is.EqualTo("test"));
    }

    [Test]
    public async Task GetAsync_NotExpiredItem_ReturnsValue()
    {
        await _service.SetAsync("fresh", "still-here", expiry: TimeSpan.FromMinutes(5));
        var result = await _service.GetAsync<string>("fresh");
        Assert.That(result, Is.EqualTo("still-here"));
    }
}
