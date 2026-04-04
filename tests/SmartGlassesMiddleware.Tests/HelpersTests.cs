using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Services;

[TestFixture]
public class HelpersTests
{
    [Test]
    public void ConfigurationWrapper_Works()
    {
        var config = new ConfigurationWrapper(3, 500);
        Assert.That(config["RAG:MaxRetries"], Is.EqualTo("3"));
        Assert.That(config["RAG:RetryDelayMs"], Is.EqualTo("500"));
        Assert.That(config["Missing"], Is.Null);

        config["Test"] = "Value";
        Assert.That(config["Test"], Is.EqualTo("Value"));

        var children = config.GetChildren();
        Assert.That(children, Is.Empty);

        var token = config.GetReloadToken();
        Assert.That(token, Is.Not.Null);

        var section = config.GetSection("TestSection");
        Assert.That(section, Is.Not.Null);
    }

    [Test]
    public void ConfigurationWrapper_HandlesEdgeCases()
    {
        // Test negative and zero constructor parameters
        var config = new ConfigurationWrapper(0, -100);
        Assert.That(config["RAG:MaxRetries"], Is.EqualTo("0"));
        Assert.That(config["RAG:RetryDelayMs"], Is.EqualTo("-100"));

        // Test getting an unset key explicitly
        Assert.That(config["NonExistentKey"], Is.Null);

        // Test setting a value to empty string and retrieving it
        config["EmptyStringKey"] = "";
        Assert.That(config["EmptyStringKey"], Is.EqualTo(""));

        // Test setting a value to null and retrieving it
        config["NullKey"] = null;
        Assert.That(config["NullKey"], Is.Null);

        // Test overwriting a value multiple times
        config["OverwriteKey"] = "FirstValue";
        Assert.That(config["OverwriteKey"], Is.EqualTo("FirstValue"));
        config["OverwriteKey"] = "SecondValue";
        Assert.That(config["OverwriteKey"], Is.EqualTo("SecondValue"));
        config["OverwriteKey"] = null;
        Assert.That(config["OverwriteKey"], Is.Null);
    }

    [Test]
    public void ConfigurationSection_Works()
    {
        var config = new ConfigurationWrapper(3, 500);
        config["Path:TestKey"] = "TestValue";

        var section = new SmartGlassesMiddleware.Services.ConfigurationSection(config, "Path");
        Assert.That(section["TestKey"], Is.EqualTo("TestValue"));

        section["TestKey"] = "NewValue";
        Assert.That(config["Path:TestKey"], Is.EqualTo("NewValue"));

        Assert.That(section.Key, Is.EqualTo("Path"));
        Assert.That(section.Path, Is.EqualTo("Path"));

        section.Value = "SectionValue";
        Assert.That(section.Value, Is.EqualTo("SectionValue"));

        var children = section.GetChildren();
        Assert.That(children, Is.Empty);

        var token = section.GetReloadToken();
        Assert.That(token, Is.Not.Null);

        var subSection = section.GetSection("Sub");
        Assert.That(subSection.Path, Is.EqualTo("Path:Sub"));
    }

    [Test]
    public void ConfigurationReloadToken_Works()
    {
        var token = new ConfigurationReloadToken();
        Assert.That(token.HasChanged, Is.False);
        Assert.That(token.ActiveChangeCallbacks, Is.False);

        var disposable = token.RegisterChangeCallback(_ => { }, null);
        Assert.That(disposable, Is.Not.Null);
    }

    [Test]
    public void EmptyDisposable_Works()
    {
        var disposable = new EmptyDisposable();
        Assert.DoesNotThrow(() => disposable.Dispose());
    }

    [Test]
    public void HttpClientFactory_Works()
    {
        var factory = new SmartGlassesMiddleware.Services.HttpClientFactory();
        var client = factory.CreateClient("test");
        Assert.That(client, Is.Not.Null);
    }
}
