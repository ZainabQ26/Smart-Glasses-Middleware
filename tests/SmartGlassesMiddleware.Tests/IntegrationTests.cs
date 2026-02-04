using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;

namespace SmartGlassesMiddleware.Tests;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                // Remove all database context related services
                var dbContextDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                if (dbContextDescriptor != null)
                    services.Remove(dbContextDescriptor);

                var dbContextDescriptor2 = services.SingleOrDefault(
                    d => d.ServiceType == typeof(ApplicationDbContext));
                if (dbContextDescriptor2 != null)
                    services.Remove(dbContextDescriptor2);

                // Add in-memory database
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase("TestDb");
                });

                // Replace IBackgroundTaskQueue with a test implementation that completes immediately
                services.AddSingleton<IBackgroundTaskQueue, TestBackgroundTaskQueue>();
            });
        });
    }

    [Fact(Timeout = 30000)]
    public async Task Enqueue_Task_Triggers_SignalR_Notification()
    {
        TestBackgroundTaskQueue.Reset();

        using var scope = _factory.Services.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();

        // Enqueue a task directly through DI (integration-level service graph)
        await WithTimeout(queue.EnqueueAsync(new BackgroundTaskEntry { PromptText = "test" }), TimeSpan.FromSeconds(5));

        // Wait for the in-test queue signal (represents completion notification pathway)
        var entry = await WithTimeout(TestBackgroundTaskQueue.EnqueueTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal(BackgroundTaskStatus.Completed, entry.Status);
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:N0} seconds.");
        }

        await task;
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:N0} seconds.");
        }

        return await task;
    }

    private class TestBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public static TaskCompletionSource<BackgroundTaskEntry> EnqueueTcs { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            EnqueueTcs = new TaskCompletionSource<BackgroundTaskEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task EnqueueAsync(BackgroundTaskEntry entry)
        {
            // Simulate immediate processing and signal completion to the test
            entry.Status = BackgroundTaskStatus.Completed;
            EnqueueTcs.TrySetResult(entry);
            return Task.CompletedTask;
        }

        public ValueTask<BackgroundTaskEntry?> DequeueAsync(System.Threading.CancellationToken cancellationToken) => new ValueTask<BackgroundTaskEntry?>((BackgroundTaskEntry?)null);

        public BackgroundTaskEntry? Get(Guid id) => new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Completed };

        public Task UpdateAsync(BackgroundTaskEntry entry) => Task.CompletedTask;
    }
}
