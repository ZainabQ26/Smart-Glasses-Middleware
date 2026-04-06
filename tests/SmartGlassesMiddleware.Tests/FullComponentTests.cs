using NUnit.Framework;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;
using System.Threading;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class FullComponentTests
    {
        private static ProcessController BuildController(
            IAIService aiService,
            IPageSummaryService pageSummaryService,
            IGroqTranscriptionLogService groqLog,
            ILogger<ProcessController> logger,
            IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(aiService);
            services.AddSingleton(pageSummaryService);
            services.AddSingleton(groqLog);
            services.AddSingleton(logger);
            services.AddSingleton(configuration);
            var sp = services.BuildServiceProvider();

            var controller = new ProcessController();
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = sp }
            };
            return controller;
        }

        [Test]
        public async Task ProcessSpeech_ValidTranscript_ReturnsMatchedHandlerAction()
        {
            var mockAiService = new Mock<IAIService>();
            mockAiService.Setup(a => a.ProcessRequestAsync("what time is it", It.IsAny<CancellationToken>(), It.IsAny<ProcessDocumentContext>()))
                         .ReturnsAsync(("time", "The current time is 12:00 PM.", false));

            var mockConfig = new Mock<IConfiguration>();
            var mockConfigSection = new Mock<IConfigurationSection>();
            mockConfigSection.Setup(x => x.Value).Returns("groq-only");
            mockConfig.Setup(x => x.GetSection("AI:SpeechTranscriptionMode")).Returns(mockConfigSection.Object);

            var mockWakeWordSection = new Mock<IConfigurationSection>();
            mockWakeWordSection.Setup(x => x.Value).Returns("Hi Iris");
            mockConfig.Setup(x => x.GetSection("AI:ActivationWord")).Returns(mockWakeWordSection.Object);

            var mockLogger = new Mock<ILogger<ProcessController>>();
            var mockPageSummary = new Mock<IPageSummaryService>();

            var controller = BuildController(
                mockAiService.Object,
                mockPageSummary.Object,
                new Mock<IGroqTranscriptionLogService>().Object,
                mockLogger.Object,
                mockConfig.Object
            );

            var req = new ProcessRequest { Input = "what time is it" };

            var result = await controller.Process(req, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var okResult = result as OkObjectResult;
            Assert.That(okResult, Is.Not.Null);

            // Check that the returned anonymous object has our expected task mapping from AI Service
            var props = okResult.Value.GetType().GetProperties();
            var taskProp = System.Array.Find(props, p => p.Name == "task");
            Assert.That(taskProp, Is.Not.Null);
            var taskVal = taskProp.GetValue(okResult.Value) as string;
            Assert.That(taskVal, Is.EqualTo("time"));
        }

        [Test]
        public async Task SummarizePage_ValidRequest_ReturnsSummary()
        {
            var mockAiService = new Mock<IAIService>();
            var mockConfig = new Mock<IConfiguration>();
            var mockLogger = new Mock<ILogger<ProcessController>>();

            var mockPageSummary = new Mock<IPageSummaryService>();
            mockPageSummary.Setup(s => s.SummarizePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync("A very good summary.");

            var controller = BuildController(
                mockAiService.Object,
                mockPageSummary.Object,
                new Mock<IGroqTranscriptionLogService>().Object,
                mockLogger.Object,
                mockConfig.Object
            );

            var req = new PageSummaryRequest { PageText = "Some dummy document text here." };

            var result = await controller.SummarizePage(req, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var okResult = result as OkObjectResult;
            Assert.That(okResult, Is.Not.Null);

            // Verify our mocked Summary was successfully orchestrated
            var props = okResult.Value.GetType().GetProperties();
            var sumProp = System.Array.Find(props, p => p.Name == "summary");
            Assert.That(sumProp, Is.Not.Null);
            var sumVal = sumProp.GetValue(okResult.Value) as string;
            Assert.That(sumVal, Is.EqualTo("A very good summary."));
        }
    }
}
