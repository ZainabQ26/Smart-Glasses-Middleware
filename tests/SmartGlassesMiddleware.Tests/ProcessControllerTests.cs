using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class ProcessControllerTests
{
    private Mock<IAIService> _aiServiceMock;
    private Mock<IPageSummaryService> _pageSummaryServiceMock;
    private Mock<IGroqTranscriptionLogService> _groqTranscriptionLogMock;
    private Mock<ILogger<ProcessController>> _loggerMock;
    private Mock<IConfiguration> _configMock;
    private ProcessController _controller;
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public void Setup()
    {
        _aiServiceMock = new Mock<IAIService>();
        _pageSummaryServiceMock = new Mock<IPageSummaryService>();
        _groqTranscriptionLogMock = new Mock<IGroqTranscriptionLogService>();
        _loggerMock = new Mock<ILogger<ProcessController>>();
        _configMock = new Mock<IConfiguration>();

        _controller = BuildController(
            _aiServiceMock.Object,
            _pageSummaryServiceMock.Object,
            _groqTranscriptionLogMock.Object,
            _loggerMock.Object,
            _configMock.Object,
            out _serviceProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }

    private static ProcessController BuildController(
        IAIService aiService,
        IPageSummaryService pageSummaryService,
        IGroqTranscriptionLogService groqTranscriptionLog,
        ILogger<ProcessController> logger,
        IConfiguration configuration,
        out ServiceProvider serviceProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(aiService);
        services.AddSingleton(pageSummaryService);
        services.AddSingleton(groqTranscriptionLog);
        services.AddSingleton(logger);
        services.AddSingleton(configuration);
        serviceProvider = services.BuildServiceProvider();

        var controller = new ProcessController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = serviceProvider }
        };
        return controller;
    }

    [Test]
    public async Task Preview_NullInput_ReturnsBadRequest()
    {
        var result = await _controller.Preview(new ProcessRequest { Input = null! }, CancellationToken.None);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Preview_ValidInput_ReturnsOkWithWillUseAdvanced()
    {
        _aiServiceMock.Setup(x => x.WillUseAdvancedModelAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var result = await _controller.Preview(new ProcessRequest { Input = "test" }, CancellationToken.None) as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value!.ToString(), Contains.Substring("willUseAdvanced = True"));
    }

    // GetClientSettings tests removed — endpoint moved to minimal API in Program.cs

    [Test]
    public async Task Process_NullInput_ReturnsBadRequest()
    {
        var result = await _controller.Process(new ProcessRequest { Input = null! }, CancellationToken.None);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Process_ValidInput_ReturnsOkWithTaskAndResponse()
    {
        _aiServiceMock.Setup(x => x.ProcessRequestAsync("test", It.IsAny<CancellationToken>(), null))
                      .ReturnsAsync(("Task", "Response", true));

        var result = await _controller.Process(new ProcessRequest { Input = "test" }, CancellationToken.None) as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value!.ToString(), Contains.Substring("usedAdvancedModel = True"));
    }

    [Test]
    public async Task Process_OperationCanceled_Returns408()
    {
        _aiServiceMock.Setup(x => x.ProcessRequestAsync("test", It.IsAny<CancellationToken>(), null))
                      .ThrowsAsync(new OperationCanceledException());

        var result = await _controller.Process(new ProcessRequest { Input = "test" }, CancellationToken.None) as ObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(408));
    }

    [Test]
    public async Task SummarizePage_NullInput_ReturnsBadRequest()
    {
        var result = await _controller.SummarizePage(new PageSummaryRequest { PageText = null! }, CancellationToken.None);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task SummarizePage_ValidInput_ReturnsOkWithSummary()
    {
        _pageSummaryServiceMock.Setup(x => x.SummarizePageAsync("text", "doc", 1, It.IsAny<CancellationToken>()))
                               .ReturnsAsync("Summary");

        var result = await _controller.SummarizePage(new PageSummaryRequest { PageText = "text", DocumentName = "doc", PageNumber = 1 }, CancellationToken.None) as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value!.ToString(), Contains.Substring("summary = Summary"));
    }
}
