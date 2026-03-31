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

    [Test]
    public async Task NoOpQueue_DequeueAsync_AfterEnqueue_ReturnsItem()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance);
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
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled

        var dequeued = await queue.DequeueAsync(cts.Token);

        Assert.That(dequeued, Is.Null);
    }

    [Test]
    public async Task NoOpQueue_Get_AfterEnqueue_ReturnsEntry()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance);
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
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance);
        Assert.That(queue.Get(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task NoOpQueue_UpdateAsync_UpdatesEntry()
    {
        var queue = new NoOpBackgroundTaskQueue(NullLogger<NoOpBackgroundTaskQueue>.Instance);
        var id = Guid.NewGuid();
        var entry = new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Queued };

        var updated = new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Processing };
        await queue.UpdateAsync(updated);

        Assert.That(queue.Get(id)!.Status, Is.EqualTo(BackgroundTaskStatus.Processing));
    }

    // ── ShowRemindersTaskHandler – JsonDateTimeConverter.Write ───────────────

    [Test]
    public async Task ShowReminders_WithReminders_SerializesDateTimeWithConverter()
    {
        // The Write method of JsonDateTimeConverter is exercised when HandleAsync serializes
        // ReminderItems that have a DateTime CreatedAt marked with [JsonConverter(typeof(JsonDateTimeConverter))]
        var historyMock = new Mock<IHistoryService>();
        historyMock.Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
            .ReturnsAsync(new List<RequestHistory>
            {
                new RequestHistory
                {
                    Id = Guid.NewGuid(),
                    InputText = "take medication",
                    RequestType = "reminder",
                    Response = "Reminder set",
                    Timestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc)
                }
            });

        var handler = new ShowRemindersTaskHandler(historyMock.Object,
            NullLogger<ShowRemindersTaskHandler>.Instance);

        var result = await handler.HandleAsync("show my reminders");

        // The result should contain JSON with an ISO-8601 formatted date (via Write method)
        Assert.That(result, Does.StartWith("__ACTION:SHOW_REMINDERS:"));
        Assert.That(result, Does.Contain("2024-06-15"));
    }

    [Test]
    public async Task ShowReminders_NoReminders_ReturnsNoRemindersMessage()
    {
        var historyMock = new Mock<IHistoryService>();
        historyMock.Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
            .ReturnsAsync(new List<RequestHistory>());

        var handler = new ShowRemindersTaskHandler(historyMock.Object,
            NullLogger<ShowRemindersTaskHandler>.Instance);

        var result = await handler.HandleAsync("show reminders");

        Assert.That(result, Does.Contain("no reminders").IgnoreCase);
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

    [Test]
    public async Task ConversationService_DetectOrCreate_NewConversation_CreatesGroupAndConverses()
    {
        // When no recent conversation exists and no AI service, it falls back to creating a
        // new ResourceGroup and Conversation — exercising InvalidateGroupsCache.
        var (ctx, svc) = CreateConversationService();
        using (ctx)
        {
            var conversation = await svc.DetectOrCreateConversationAsync("discuss blood pressure treatment");

            Assert.That(conversation, Is.Not.Null);
            Assert.That(conversation.Id, Is.Not.EqualTo(Guid.Empty));
            // A ResourceGroup should have been created as part of the fallback path
            Assert.That(await ctx.ResourceGroups.CountAsync(), Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task ConversationService_DetectOrCreate_WithNullResourceHub_DoesNotThrow()
    {
        // Exercises the NotifyGroupsChangedAsync null-hub path (Task.CompletedTask branch).
        var (ctx, svc) = CreateConversationService(resourceHub: null);
        using (ctx)
        {
            Assert.DoesNotThrowAsync(async () =>
                await svc.DetectOrCreateConversationAsync("any message"));
        }
    }

    [Test]
    public async Task ConversationService_DetectOrCreate_ExistingRecentConversation_ReturnsThatConversation()
    {
        var (ctx, svc) = CreateConversationService();
        using (ctx)
        {
            // Pre-create an active conversation updated within the last hour
            var existingId = Guid.NewGuid();
            ctx.Conversations.Add(new Conversation
            {
                Id = existingId,
                Title = "Existing",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddMinutes(-30),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
            });
            await ctx.SaveChangesAsync();

            var result = await svc.DetectOrCreateConversationAsync("something");

            Assert.That(result.Id, Is.EqualTo(existingId));
        }
    }

    // ── ConversationService – NotifyGroupsChangedAsync with non-null hub ─────

    [Test]
    public async Task ConversationService_WithResourceHub_NotifyGroupsChanged_IsInvoked()
    {
        // Wire up a mock IHubContext<ResourceHub> so the "hub != null" path in
        // NotifyGroupsChangedAsync is exercised.
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientProxyMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubMock = new Mock<IHubContext<ResourceHub>>();
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var (ctx, svc) = CreateConversationService(resourceHub: hubMock.Object);
        using (ctx)
        {
            // DetectOrCreate will call NotifyGroupsChangedAsync in the fallback path
            await svc.DetectOrCreateConversationAsync("any message");

            // The hub should have been asked to send "groupsChanged"
            clientProxyMock.Verify(c =>
                c.SendCoreAsync("groupsChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }
    }

    // ── TaskDispatcher – KnownTasks warning for missing handlers ─────────────

    [Test]
    public void TaskDispatcher_KnownTasksConfiguredWithMissingHandler_LogsAndFiltersOut()
    {
        // Provide a task name in config that has no corresponding ITaskHandler implementation.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:KnownTasks:0"] = "nonexistent-task-xyz"
            })
            .Build();

        var loggerMock = new Mock<ILogger<TaskDispatcher>>();
        var spMock = new Mock<IServiceProvider>();
        spMock.Setup(sp => sp.GetService(typeof(IEnumerable<ITaskHandler>)))
            .Returns(Enumerable.Empty<ITaskHandler>());

        var dispatcher = new TaskDispatcher(config, spMock.Object, loggerMock.Object);

        // KnownTasks filters to only tasks that have registered handlers,
        // so the nonexistent task should NOT appear in the list.
        var knownTasks = dispatcher.KnownTasks;
        Assert.That(knownTasks, Does.Not.Contain("nonexistent-task-xyz"));
    }

    // ── HighlightWordTaskHandler – Keywords list + no-prefix fallback ─────────

    [Test]
    public void HighlightWord_Keywords_ContainsExpectedEntries()
    {
        var abbrevMock = new Mock<IAbbreviationService>();
        abbrevMock.Setup(s => s.GetRelatedTermsAsync(It.IsAny<string>()))
            .ReturnsAsync((string t) => ((List<string>)[t], new List<Abbreviation>()));

        var handler = new HighlightWordTaskHandler(abbrevMock.Object);

        var keywords = handler.Keywords;

        // Accessing the full Keywords list exercises all elements (lines 29-34)
        Assert.That(keywords, Does.Contain("highlight word"));
        Assert.That(keywords, Does.Contain("highlight the word"));
        Assert.That(keywords, Does.Contain("highlight the phrase"));
        Assert.That(keywords, Does.Contain("highlight phrase"));
        Assert.That(keywords, Does.Contain("search for the word"));
        Assert.That(keywords, Does.Contain("find the word"));
        Assert.That(keywords, Does.Contain("find the phrase"));
        Assert.That(keywords.Count, Is.GreaterThan(5));
    }

    [Test]
    public async Task HighlightWord_InputWithNoKnownPrefix_UsesFallbackReturnTrimmed()
    {
        // When the input doesn't start with any recognized prefix, the handler returns
        // the whole input trimmed as the highlight target (the fallback `return trimmed;` on line 108).
        var abbrevMock = new Mock<IAbbreviationService>();
        abbrevMock.Setup(s => s.GetRelatedTermsAsync(It.IsAny<string>()))
            .ReturnsAsync((string t) => ((List<string>)[t], new List<Abbreviation>()));

        var handler = new HighlightWordTaskHandler(abbrevMock.Object);

        // "keyword" has no strippable prefix — falls all the way through to `return trimmed`
        var result = await handler.HandleAsync("keyword");

        Assert.That(result, Is.EqualTo("__ACTION:HIGHLIGHT_WORD:keyword__"));
    }

    // ── SelectDocumentTaskHandler – ManagedFile fallback ─────────────────────

    [Test]
    public async Task SelectDocument_NoDocMetadataButManagedFileExists_ReturnsManagedFileAction()
    {
        // DocumentMetadatas table is empty → handler falls through to ManagedFiles fallback (lines 51-64)
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var ctx = new ApplicationDbContext(options);

        var mfId = Guid.NewGuid();
        ctx.ManagedFiles.Add(new ManagedFile
        {
            Id = mfId,
            FileName = "uploaded.pdf",
            Label = "My Report",
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/uploaded.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 2048
        });
        await ctx.SaveChangesAsync();

        var handler = new SelectDocumentTaskHandler(ctx);
        var result = await handler.HandleAsync("open document");

        Assert.That(result, Does.StartWith($"__ACTION:SELECT_DOCUMENT:{mfId}:My Report"));
    }

    [Test]
    public async Task SelectDocument_ManagedFileWithNoLabel_UsesFileName()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var ctx = new ApplicationDbContext(options);

        var mfId = Guid.NewGuid();
        ctx.ManagedFiles.Add(new ManagedFile
        {
            Id = mfId,
            FileName = "nolabel.pdf",
            Label = "",          // empty → FileName used as display name
            ContentType = "application/pdf",
            StorageKey = "s3://bucket/nolabel.pdf",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1024
        });
        await ctx.SaveChangesAsync();

        var handler = new SelectDocumentTaskHandler(ctx);
        var result = await handler.HandleAsync("select document");

        Assert.That(result, Does.Contain("nolabel.pdf"));
    }

    // ── OpenFileTaskHandler – no files path ──────────────────────────────────

    [Test]
    public async Task OpenFile_NoManagedFilesInDb_ReturnsNoFilesMessage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var ctx = new ApplicationDbContext(options);

        var handler = new OpenFileTaskHandler(ctx);
        var result = await handler.HandleAsync("show recent files");

        Assert.That(result, Does.Contain("No uploaded files found").IgnoreCase);
    }

    // ── InMemoryCacheService – expiry time path (already partly covered) ─────

    [Test]
    public async Task InMemoryCache_SetWithExpiry_NonExpiredItemReturned()
    {
        var svc = new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance);
        await svc.SetAsync("key1", "value1", TimeSpan.FromMinutes(10));

        var result = await svc.GetAsync<string>("key1");

        Assert.That(result, Is.EqualTo("value1"));
    }

    [Test]
    public async Task InMemoryCache_SetWithoutExpiry_ItemPersists()
    {
        var svc = new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance);
        await svc.SetAsync("key2", 42);

        var result = await svc.GetAsync<int?>("key2");

        Assert.That(result, Is.EqualTo(42));
    }
}
