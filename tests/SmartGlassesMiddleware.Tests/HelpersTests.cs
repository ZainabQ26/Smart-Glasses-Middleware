using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class HelpersTests
{
    [Fact]
    public void HttpClientFactory_CreateClient_Returns_HttpClient()
    {
        // Arrange
        var factory = new HttpClientFactory();

        // Act
        var client = factory.CreateClient("test");

        // Assert
        Assert.NotNull(client);
        Assert.IsType<HttpClient>(client);
    }

    [Fact]
    public void ConfigurationWrapper_Constructor_Sets_Values()
    {
        // Arrange & Act
        var config = new ConfigurationWrapper(3, 1000);

        // Assert
        Assert.Equal("3", config["RAG:MaxRetries"]);
        Assert.Equal("1000", config["RAG:RetryDelayMs"]);
    }

    [Fact]
    public void ConfigurationWrapper_Indexer_Get_Returns_Null_For_NonExistent_Key()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);

        // Act
        var value = config["NonExistent:Key"];

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void ConfigurationWrapper_Indexer_Set_And_Get_Works()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);

        // Act
        config["Custom:Setting"] = "TestValue";
        var value = config["Custom:Setting"];

        // Assert
        Assert.Equal("TestValue", value);
    }

    [Fact]
    public void ConfigurationWrapper_GetChildren_Returns_Empty()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);

        // Act
        var children = config.GetChildren();

        // Assert
        Assert.Empty(children);
    }

    [Fact]
    public void ConfigurationWrapper_GetReloadToken_Returns_Token()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);

        // Act
        var token = config.GetReloadToken();

        // Assert
        Assert.NotNull(token);
        Assert.IsAssignableFrom<IChangeToken>(token);
    }

    [Fact]
    public void ConfigurationWrapper_GetSection_Returns_Section()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);

        // Act
        var section = config.GetSection("RAG");

        // Assert
        Assert.NotNull(section);
        Assert.Equal("RAG", section.Key);
    }

    [Fact]
    public void ConfigurationSection_Key_Returns_Last_Part_Of_Path()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);
        var section = new Services.ConfigurationSection(config, "RAG:MaxRetries");

        // Act
        var key = section.Key;

        // Assert
        Assert.Equal("MaxRetries", key);
    }

    [Fact]
    public void ConfigurationSection_Path_Returns_Full_Path()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);
        var section = new Services.ConfigurationSection(config, "RAG:MaxRetries");

        // Act
        var path = section.Path;

        // Assert
        Assert.Equal("RAG:MaxRetries", path);
    }

    [Fact]
    public void ConfigurationSection_Value_Get_Returns_Config_Value()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);
        var section = new Services.ConfigurationSection(config, "RAG:MaxRetries");

        // Act
        var value = section.Value;

        // Assert
        Assert.Equal("3", value);
    }

    [Fact]
    public void ConfigurationSection_Value_Set_Updates_Config()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);
        var section = new Services.ConfigurationSection(config, "RAG:MaxRetries");

        // Act
        section.Value = "5";
        var value = config["RAG:MaxRetries"];

        // Assert
        Assert.Equal("5", value);
    }

    [Fact]
    public void ConfigurationSection_Indexer_Get_Returns_Nested_Value()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);
        var section = new Services.ConfigurationSection(config, "RAG");

        // Act
        var value = section["MaxRetries"];

        // Assert
        Assert.Equal("3", value);
    }

    [Fact]
    public void ConfigurationSection_Indexer_Set_Updates_Nested_Value()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);
        var section = new Services.ConfigurationSection(config, "RAG");

        // Act
        section["MaxRetries"] = "7";
        var value = config["RAG:MaxRetries"];

        // Assert
        Assert.Equal("7", value);
    }

    [Fact]
    public void ConfigurationSection_GetSection_Returns_Nested_Section()
    {
        // Arrange
        var config = new ConfigurationWrapper(3, 1000);
        var section = new Services.ConfigurationSection(config, "RAG");

        // Act
        var nested = section.GetSection("MaxRetries");

        // Assert
        Assert.NotNull(nested);
        Assert.Equal("RAG:MaxRetries", nested.Path);
    }

    [Fact]
    public void ConfigurationReloadToken_HasChanged_Returns_False()
    {
        // Arrange
        var token = new Services.ConfigurationReloadToken();

        // Act & Assert
        Assert.False(token.HasChanged);
    }

    [Fact]
    public void ConfigurationReloadToken_ActiveChangeCallbacks_Returns_False()
    {
        // Arrange
        var token = new Services.ConfigurationReloadToken();

        // Act & Assert
        Assert.False(token.ActiveChangeCallbacks);
    }

    [Fact]
    public void ConfigurationReloadToken_RegisterChangeCallback_Returns_Disposable()
    {
        // Arrange
        var token = new Services.ConfigurationReloadToken();
        var callbackInvoked = false;

        // Act
        var disposable = token.RegisterChangeCallback(_ => callbackInvoked = true, null);

        // Assert
        Assert.NotNull(disposable);
        Assert.IsType<EmptyDisposable>(disposable);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void EmptyDisposable_Dispose_Does_Not_Throw()
    {
        // Arrange
        var disposable = new EmptyDisposable();

        // Act & Assert
        var exception = Record.Exception(() => disposable.Dispose());
        Assert.Null(exception);
    }
}
