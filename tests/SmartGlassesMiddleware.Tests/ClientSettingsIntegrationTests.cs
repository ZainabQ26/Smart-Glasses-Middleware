using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Integration tests for the /api/process/client-settings minimal API endpoint.
/// Uses WebApplicationFactory to spin up the full middleware pipeline so routing,
/// middleware, and DI are all exercised — the exact path a real HTTP request takes.
/// </summary>
[TestFixture]
public class ClientSettingsIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    // Remove all IHostedService registrations so background services
                    // (S3FileIndexService, AbbreviationSyncBackgroundService, etc.)
                    // don't try to reach external systems during tests.
                    var hostedServices = services
                        .Where(d => d.ServiceType == typeof(IHostedService))
                        .ToList();
                    foreach (var svc in hostedServices)
                        services.Remove(svc);
                });
            });
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetClientSettings_Returns200()
    {
        var response = await _client.GetAsync("/api/process/client-settings");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetClientSettings_ReturnsExpectedShape()
    {
        var response = await _client.GetAsync("/api/process/client-settings");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("speechTranscriptionMode", out _), Is.True,
            "Response must include speechTranscriptionMode");
        Assert.That(root.TryGetProperty("activationWord", out _), Is.True,
            "Response must include activationWord");
        Assert.That(root.TryGetProperty("groqCommandTranscriptionEnabled", out _), Is.True,
            "Response must include groqCommandTranscriptionEnabled");
    }

    [Test]
    public async Task GetClientSettings_DefaultValues_WhenNoConfigSet()
    {
        var response = await _client.GetAsync("/api/process/client-settings");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Default mode should be "groq-only" when no config is provided
        var mode = root.GetProperty("speechTranscriptionMode").GetString();
        Assert.That(mode, Is.EqualTo("groq-only"));

        // Default activation word should be "iris" when no config is provided
        var activationWord = root.GetProperty("activationWord").GetString();
        Assert.That(activationWord, Is.EqualTo("iris"));

        // groqCommandTranscriptionEnabled should always be true
        var groqEnabled = root.GetProperty("groqCommandTranscriptionEnabled").GetBoolean();
        Assert.That(groqEnabled, Is.True);
    }
}
