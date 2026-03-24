using NUnit.Framework;
using Moq;
using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class AdminControllerTests
    {
        private AdminController _controller;
        private Mock<IMediaStorageService> _mockMediaStorage;
        private Mock<ICacheService> _mockCache;
        private Mock<IRagService> _mockRagService;
        private Mock<IRAGService> _mockDocRagService;
        private Mock<ILogger<AdminController>> _mockLogger;
        private Mock<IRequestMonitoringService> _mockMonitoring;
        private Mock<IServerSettingsService> _mockSettings;
        private Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory> _mockScopeFactory;

        [SetUp]
        public void Setup()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "AdminControllerTestsDb")
                .Options;

            var dbContext = new ApplicationDbContext(options);

            _mockMediaStorage = new Mock<IMediaStorageService>();
            _mockCache = new Mock<ICacheService>();
            _mockRagService = new Mock<IRagService>();
            _mockDocRagService = new Mock<IRAGService>();
            _mockLogger = new Mock<ILogger<AdminController>>();
            _mockMonitoring = new Mock<IRequestMonitoringService>();
            _mockSettings = new Mock<IServerSettingsService>();
            _mockScopeFactory = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

            _controller = new AdminController(
                dbContext,
                _mockMediaStorage.Object,
                _mockCache.Object,
                _mockRagService.Object,
                _mockDocRagService.Object,
                _mockLogger.Object,
                _mockMonitoring.Object,
                _mockSettings.Object,
                _mockScopeFactory.Object
            );
        }

        [Test]
        public async Task GetPreventSleepSetting_ReturnsExpected()
        {
            _mockSettings.Setup(s => s.GetPreventSleepStateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SmartGlassesMiddleware.Services.PreventSleepSettingState(true, false, DateTime.UtcNow));

            var result = await _controller.GetPreventSleepSetting(CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());

            var okResult = result.Result as OkObjectResult;
            Assert.That(okResult, Is.Not.Null);
            Assert.That(okResult.Value, Is.Not.Null);
        }

        [Test]
        public async Task ReindexAll_CallsDocumentRagService()
        {
            _mockDocRagService.Setup(s => s.ReindexAllDocumentsAsync(It.IsAny<bool>())).ReturnsAsync(5);

            var result = await _controller.ReindexAll(force: true);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            _mockDocRagService.Verify(s => s.ReindexAllDocumentsAsync(true), Times.Once);
        }
    }
}
