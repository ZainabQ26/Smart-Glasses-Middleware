using NUnit.Framework;
using Moq;
using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Threading;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class ProcessControllerTests
    {
        private ProcessController _controller;
        private Mock<IAIService> _mockAiService;
        private Mock<ILogger<ProcessController>> _mockLogger;
        private Mock<IConfiguration> _mockConfiguration;
        private Mock<IPageSummaryService> _mockPageSummary;

        [SetUp]
        public void Setup()
        {
            _mockAiService = new Mock<IAIService>();
            _mockLogger = new Mock<ILogger<ProcessController>>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockPageSummary = new Mock<IPageSummaryService>();

            var mockConfigSection = new Mock<IConfigurationSection>();
            mockConfigSection.Setup(x => x.Value).Returns("whisper-only");
            _mockConfiguration.Setup(x => x.GetSection("AI:SpeechTranscriptionMode")).Returns(mockConfigSection.Object);

            var mockWakeWordSection = new Mock<IConfigurationSection>();
            mockWakeWordSection.Setup(x => x.Value).Returns("Iris");
            _mockConfiguration.Setup(x => x.GetSection("AI:ActivationWord")).Returns(mockWakeWordSection.Object);

            _controller = new ProcessController(
                _mockAiService.Object,
                _mockPageSummary.Object,
                _mockLogger.Object,
                _mockConfiguration.Object
            );
        }

        [Test]
        public void GetClientSettings_ReturnsConfiguredValues()
        {
            var result = _controller.GetClientSettings();

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var okResult = result as OkObjectResult;
            Assert.That(okResult, Is.Not.Null);
        }

        [Test]
        public async Task PostProcess_EmptyTranscript_ReturnsBadRequest()
        {
            var req = new ProcessRequest { Input = "   " };
            var result = await _controller.Process(req, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task SummarizePage_EmptyPageText_ReturnsBadRequest()
        {
            var req = new PageSummaryRequest { PageText = "   " };
            var result = await _controller.SummarizePage(req, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }
    }
}
