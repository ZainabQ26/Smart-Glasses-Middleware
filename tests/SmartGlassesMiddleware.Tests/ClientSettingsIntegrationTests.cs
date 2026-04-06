using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
    public async Task GetClientSettings_ReflectsConfiguredValues()
    {
        var response = await _client.GetAsync("/api/process/client-settings");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        var configuredMode = configuration["AI:SpeechTranscriptionMode"]?.Trim().ToLowerInvariant();
        var expectedMode = configuredMode switch
        {
            "webspeech-only" => "webspeech-only",
            "whisper-only" => "whisper-only",
            "groq-only" => "groq-only",
            _ => "groq-only"
        };

        var configuredActivationWord = configuration["AI:ActivationWord"]?.Trim().ToLowerInvariant();
        var expectedActivationWord = string.IsNullOrWhiteSpace(configuredActivationWord)
            ? "hello iris"
            : configuredActivationWord;

        var mode = root.GetProperty("speechTranscriptionMode").GetString();
        Assert.That(mode, Is.EqualTo(expectedMode));

        var activationWord = root.GetProperty("activationWord").GetString()?.ToLowerInvariant();
        Assert.That(activationWord, Is.EqualTo(expectedActivationWord));

        var groqEnabled = root.GetProperty("groqCommandTranscriptionEnabled").GetBoolean();
        Assert.That(groqEnabled, Is.True);
    }
}
