using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Hubs;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using System.Text.Json;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Miscellaneous tests targeting small coverage gaps:
/// - NoOpBackgroundTaskQueue.DequeueAsync
/// - ShowRemindersTaskHandler JSON serialization (JsonDateTimeConverter.Write)
/// - ConversationService.DetectOrCreateConversationAsync (exercises InvalidateGroupsCache)
/// - ConversationService with non-null ResourceHub (exercises NotifyGroupsChangedAsync hub path)
/// - TaskDispatcher KnownTasks missing-handler warning
/// - HighlightWordTaskHandler Keywords list + no-prefix fallback
/// - SelectDocumentTaskHandler ManagedFile fallback
/// </summary>
[TestFixture]
public class MiscCoverageTests
{
    // ── NoOpBackgroundTaskQueue ──────────────────────────────────────────────

    private IConfiguration CreateMockConfig()
    {
        var inMemorySettings = new Dictionary<string, string?> {
            {"BackgroundTasks:DelayBetweenChecksMs", "100"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return configuration;
    }

    [Test]
    public async Task NoOpQueue_DequeueAsync_AfterEnqueue_ReturnsItem()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance, CreateMockConfig());
        var entry = new BackgroundTaskEntry { Id = Guid.NewGuid(), Status = BackgroundTaskStatus.Queued };

        await queue.EnqueueAsync(entry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var dequeued = await queue.DequeueAsync(cts.Token);

        Assert.That(dequeued, Is.Not.Null);
        Assert.That(dequeued!.Id, Is.EqualTo(entry.Id));
    }

    [Test]
    public async Task NoOpQueue_DequeueAsync_WhenCancelled_ReturnsNull()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance, CreateMockConfig());

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled

        var dequeued = await queue.DequeueAsync(cts.Token);

        Assert.That(dequeued, Is.Null);
    }

    [Test]
    public async Task NoOpQueue_Get_AfterEnqueue_ReturnsEntry()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance, CreateMockConfig());
        var id = Guid.NewGuid();
        var entry = new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Queued };

        await queue.EnqueueAsync(entry);

        var fetched = queue.Get(id);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.Id, Is.EqualTo(id));
    }

    [Test]
    public void NoOpQueue_Get_UnknownId_ReturnsNull()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance, CreateMockConfig());
        Assert.That(queue.Get(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task NoOpQueue_UpdateAsync_UpdatesEntry()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance, CreateMockConfig());
        var id = Guid.NewGuid();
        var entry = new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Queued };

        var updated = new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Processing };
        await queue.UpdateAsync(updated);

        Assert.That(queue.Get(id)!.Status, Is.EqualTo(BackgroundTaskStatus.Processing));
    }


    // ── ConversationService – InvalidateGroupsCache via DetectOrCreate ───────

    private static (ApplicationDbContext ctx, ConversationService svc) CreateConversationService(
        IHubContext<ResourceHub>? resourceHub = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var ctx = new ApplicationDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG:ApproximateTokensPerChar"] = "0.25"
            })
            .Build();

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        cacheMock.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);
        cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>())).Returns(Task.CompletedTask);

        var svc = new ConversationService(
            ctx,
            NullLogger<ConversationService>.Instance,
            config,
            new Mock<IServiceProvider>().Object,
            cacheMock.Object,
            resourceHub: resourceHub);

        return (ctx, svc);
    }

}
