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

        var subSubSection = subSection.GetSection("SubSub");
        Assert.That(subSubSection.Path, Is.EqualTo("Path:Sub:SubSub"));
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
