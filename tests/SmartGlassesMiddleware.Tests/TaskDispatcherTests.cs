using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Services;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

public class TaskDispatcherTests
{
    [Fact]
    public async Task Dispatcher_Uses_Configured_Tasks_When_Present()
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "AI:KnownTasks:0", "a" }
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var handlerA = new DummyHandler("a", "response-a");
        var handlerB = new DummyHandler("b", "response-b");

        var dispatcher = new TaskDispatcher(config, new[] { handlerA, handlerB });

        Assert.Contains("a", dispatcher.KnownTasks);
        Assert.DoesNotContain("b", dispatcher.KnownTasks);

        var (handled, resp) = await dispatcher.TryHandleAsync("a", "input");
        Assert.True(handled);
        Assert.Equal("response-a", resp);

        var (handled2, resp2) = await dispatcher.TryHandleAsync("b", "input");
        Assert.False(handled2);
        Assert.Null(resp2);
    }

    [Fact]
    public async Task Dispatcher_Uses_All_Handlers_When_No_Config()
    {
        IConfiguration config = new ConfigurationBuilder().Build();

        var handlerA = new DummyHandler("a", "response-a");
        var handlerB = new DummyHandler("b", "response-b");

        var dispatcher = new TaskDispatcher(config, new[] { handlerA, handlerB });

        Assert.Contains("a", dispatcher.KnownTasks);
        Assert.Contains("b", dispatcher.KnownTasks);

        var (handled, resp) = await dispatcher.TryHandleAsync("b", "input");
        Assert.True(handled);
        Assert.Equal("response-b", resp);
    }

    private class DummyHandler : ITaskHandler
    {
        private readonly string _resp;
        public DummyHandler(string name, string resp)
        {
            Name = name;
            _resp = resp;
        }

        public string Name { get; }

        public Task<string> HandleAsync(string input) => Task.FromResult(_resp);
    }
}
