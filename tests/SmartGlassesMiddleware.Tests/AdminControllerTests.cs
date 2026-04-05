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
        private Mock<ILogger<AdminController>> _mockLogger;
        private Mock<IRequestMonitoringService> _mockMonitoring;
        private Mock<IGroqTranscriptionLogService> _mockGroqLog;
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
            _mockLogger = new Mock<ILogger<AdminController>>();
            _mockMonitoring = new Mock<IRequestMonitoringService>();
            _mockGroqLog = new Mock<IGroqTranscriptionLogService>();
            _mockScopeFactory = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

            _controller = new AdminController(
                dbContext,
                _mockMediaStorage.Object,
                _mockCache.Object,
                _mockLogger.Object,
                _mockMonitoring.Object,
                _mockGroqLog.Object,
                _mockScopeFactory.Object
            );
        }

        [Test]
        public void GetGroqTranscriptions_ReturnsEntries()
        {
            _mockGroqLog.Setup(s => s.GetRecent(It.IsAny<int>()))
                .Returns(new List<GroqTranscriptionEntry>
                {
                    new() { Text = "hello world", Type = "wakeword", TimestampUtc = DateTime.UtcNow }
                });

            var result = _controller.GetGroqTranscriptions();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        }

        [Test]
        public async Task ReindexAll_ReturnsOkResult()
        {

            var result = await _controller.ReindexAll(force: true);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
        }
    }
}
