using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Hubs;
using SmartGlassesMiddleware.Data;

namespace SmartGlassesMiddleware.Tests;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
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

    [Fact]
    public async Task Enqueue_Task_Triggers_SignalR_Notification()
    {
        using var client = _factory.CreateClient();

        // Connect SignalR to the test server
        var hubUrl = new Uri(client.BaseAddress ?? new Uri("http://localhost"), "/hubs/tasks").ToString();

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options => { options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler(); })
            .WithAutomaticReconnect()
            .Build();

        var tcs = new TaskCompletionSource<string?>();
        connection.On<object>("TaskCompleted", payload =>
        {
            tcs.TrySetResult(payload?.ToString());
        });

        await connection.StartAsync();

        // Trigger the API that enqueues a task (minimal empty ask)
        var response = await client.PostAsync("/api/ask", new MultipartFormDataContent());
        response.EnsureSuccessStatusCode();

        // Wait for notification
        var notified = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(notified == tcs.Task, "SignalR notification not received");

        await connection.StopAsync();
    }

    private class TestBackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly IHubContext<TaskHub> _hub;

        public TestBackgroundTaskQueue(IHubContext<TaskHub> hub)
        {
            _hub = hub;
        }

        public Task EnqueueAsync(BackgroundTaskEntry entry)
        {
            // Simulate immediate processing and notify via SignalR
            entry.Status = BackgroundTaskStatus.Completed;
            var payload = new { id = entry.Id, status = entry.Status.ToString() };
            _ = _hub.Clients.All.SendAsync("TaskCompleted", payload);
            return Task.CompletedTask;
        }

        public ValueTask<BackgroundTaskEntry?> DequeueAsync(System.Threading.CancellationToken cancellationToken) => new ValueTask<BackgroundTaskEntry?>((BackgroundTaskEntry?)null);

        public BackgroundTaskEntry? Get(Guid id) => new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Completed };

        public Task UpdateAsync(BackgroundTaskEntry entry) => Task.CompletedTask;
    }
}
