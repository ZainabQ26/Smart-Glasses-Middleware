using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class InMemoryCacheServiceTests
{
    private InMemoryCacheService _service;

    [SetUp]
    public void Setup()
    {
        _service = new InMemoryCacheService(new Mock<ILogger<InMemoryCacheService>>().Object);
    }

    [Test]
    public async Task GetSetRemove_Works()
    {
        var key = "test-key";
        var val = "test-val";

        await _service.SetAsync(key, val, TimeSpan.FromMinutes(1));
        var retrieved = await _service.GetAsync<string>(key);
        Assert.That(retrieved, Is.EqualTo(val));

        await _service.RemoveAsync(key);
        var missing = await _service.GetAsync<string>(key);
        Assert.That(missing, Is.Null);
    }
}
