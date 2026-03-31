using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

[TestFixture]
public class AdvancedTaskHandlerTests
{
    private Mock<IAmazonBedrockRuntime> _bedrockMock = null!;
    private Mock<ILogger<AdvancedTaskHandler>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _bedrockMock = new Mock<IAmazonBedrockRuntime>();
        _loggerMock = new Mock<ILogger<AdvancedTaskHandler>>();
    }

    private AdvancedTaskHandler CreateHandler(Dictionary<string, string?> configValues)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        return new AdvancedTaskHandler(_bedrockMock.Object, config, _loggerMock.Object);
    }

    [Test]
    public void Name_ShouldBeAdvanced()
    {
        var handler = CreateHandler(new Dictionary<string, string?>());
        Assert.That(handler.Name, Is.EqualTo("advanced"));
    }

    [Test]
    public void Keywords_ShouldBeEmpty()
    {
        var handler = CreateHandler(new Dictionary<string, string?>());
        Assert.That(handler.Keywords, Is.Empty);
    }

    [Test]
    public async Task HandleAsync_ModelNotConfigured_ReturnsConfiguredMessage()
    {
        var handler = CreateHandler(new Dictionary<string, string?>());

        var result = await handler.HandleAsync("some input");

        Assert.That(result, Is.EqualTo(AdvancedTaskHandler.ModelNotConfiguredMessage));
    }

    [Test]
    public async Task HandleAsync_ModelNotConfigured_WhenOnlyWhitespace_ReturnsConfiguredMessage()
    {
        var handler = CreateHandler(new Dictionary<string, string?>
        {
            ["AWS:Bedrock:AdvancedModelId"] = "   "
        });

        var result = await handler.HandleAsync("some input");

        Assert.That(result, Is.EqualTo(AdvancedTaskHandler.ModelNotConfiguredMessage));
    }

    [Test]
    public async Task HandleAsync_BedrockThrowsException_ReturnsInvocationFailedMessage()
    {
        _bedrockMock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("test error"));

        var handler = CreateHandler(new Dictionary<string, string?>
        {
            ["AWS:Bedrock:ModelId"] = "amazon.titan-text-lite-v1"
        });

        var result = await handler.HandleAsync("hello");

        Assert.That(result, Is.EqualTo(AdvancedTaskHandler.InvocationFailedMessage));
    }

    [Test]
    public async Task HandleAsync_BedrockReturnsEmptyContent_ReturnsEmptyResponseMessage()
    {
        var response = new ConverseResponse
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Content = new List<ContentBlock>()
                }
            }
        };

        _bedrockMock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var handler = CreateHandler(new Dictionary<string, string?>
        {
            ["AWS:Bedrock:AdvancedModelId"] = "amazon.titan-text-lite-v1"
        });

        var result = await handler.HandleAsync("hello");

        Assert.That(result, Is.EqualTo(AdvancedTaskHandler.EmptyResponseMessage));
    }

    [Test]
    public async Task HandleAsync_BedrockReturnsValidResponse_ReturnsText()
    {
        var response = new ConverseResponse
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Content = new List<ContentBlock>
                    {
                        new ContentBlock { Text = "Hello from model" }
                    }
                }
            }
        };

        _bedrockMock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var handler = CreateHandler(new Dictionary<string, string?>
        {
            ["AWS:Bedrock:AdvancedModelId"] = "amazon.titan-text-lite-v1"
        });

        var result = await handler.HandleAsync("hello");

        Assert.That(result, Is.EqualTo("Hello from model"));
    }

    [Test]
    public async Task HandleAsync_UsesAdvancedModelIdOverModelId()
    {
        _bedrockMock
            .Setup(b => b.ConverseAsync(
                It.Is<ConverseRequest>(r => r.ModelId == "advanced-model-id"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("called with advanced model"));

        var handler = CreateHandler(new Dictionary<string, string?>
        {
            ["AWS:Bedrock:AdvancedModelId"] = "advanced-model-id",
            ["AWS:Bedrock:ModelId"] = "base-model-id"
        });

        var result = await handler.HandleAsync("test");

        Assert.That(result, Is.EqualTo(AdvancedTaskHandler.InvocationFailedMessage));
        _bedrockMock.Verify(
            b => b.ConverseAsync(It.Is<ConverseRequest>(r => r.ModelId == "advanced-model-id"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleAsync_FallsBackToModelId_WhenAdvancedModelIdNotSet()
    {
        _bedrockMock
            .Setup(b => b.ConverseAsync(
                It.Is<ConverseRequest>(r => r.ModelId == "base-model-id"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("called with base model"));

        var handler = CreateHandler(new Dictionary<string, string?>
        {
            ["AWS:Bedrock:ModelId"] = "base-model-id"
        });

        var result = await handler.HandleAsync("test");

        Assert.That(result, Is.EqualTo(AdvancedTaskHandler.InvocationFailedMessage));
        _bedrockMock.Verify(
            b => b.ConverseAsync(It.Is<ConverseRequest>(r => r.ModelId == "base-model-id"), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
