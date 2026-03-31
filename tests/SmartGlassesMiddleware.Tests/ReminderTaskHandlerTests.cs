using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class ReminderTaskHandlerTests
    {
        private Mock<IHistoryService> _mockHistory = null!;
        private Mock<ILogger<ReminderTaskHandler>> _mockLogger = null!;
        private Mock<IRequestMonitoringService> _mockMonitoring = null!;
        private ReminderTaskHandler _handler = null!;

        [SetUp]
        public void Setup()
        {
            _mockHistory = new Mock<IHistoryService>();
            _mockLogger = new Mock<ILogger<ReminderTaskHandler>>();
            _mockMonitoring = new Mock<IRequestMonitoringService>();

            _mockHistory
                .Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
                .ReturnsAsync((RequestHistory h) =>
                {
                    h.Id = Guid.NewGuid();
                    return h;
                });

            _mockMonitoring
                .Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("mon-id");
            _mockMonitoring
                .Setup(m => m.UpdateRequestStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
                .Returns(Task.CompletedTask);
            _mockMonitoring
                .Setup(m => m.CompleteRequest(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            _handler = new ReminderTaskHandler(_mockHistory.Object, _mockLogger.Object);
        }

        [Test]
        public void Name_ShouldBeReminder()
        {
            Assert.That(_handler.Name, Is.EqualTo("reminder"));
        }

        [Test]
        public void Keywords_ShouldContainRemindMe()
        {
            Assert.That(_handler.Keywords, Does.Contain("remind me"));
        }

        [Test]
        public void Keywords_ShouldNotBeEmpty()
        {
            Assert.That(_handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public async Task HandleAsync_EmptyInput_ReturnsDidNotCatchMessage()
        {
            var result = await _handler.HandleAsync(string.Empty);
            Assert.That(result, Does.Contain("didn't catch"));
        }

        [Test]
        public async Task HandleAsync_TooLongInput_ReturnsTooLongMessage()
        {
            var longInput = new string('x', 1001);
            var result = await _handler.HandleAsync(longInput);
            Assert.That(result, Does.Contain("too long"));
        }

        [Test]
        public async Task HandleAsync_ValidReminder_SavesViaHistoryService()
        {
            var result = await _handler.HandleAsync("buy milk");
            _mockHistory.Verify(h => h.SaveRequestAsync(It.Is<RequestHistory>(r =>
                r.RequestType == "reminder" && r.InputText == "buy milk")), Times.Once);
        }

        [Test]
        public async Task HandleAsync_ValidReminder_ReturnsMessageContainingInput()
        {
            var result = await _handler.HandleAsync("buy milk");
            Assert.That(result, Does.Contain("buy milk"));
        }

        [Test]
        public async Task HandleAsync_SaveThrows_ReturnsCouldNotSaveMessage()
        {
            _mockHistory
                .Setup(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()))
                .ThrowsAsync(new Exception("DB failure"));

            var result = await _handler.HandleAsync("buy milk");
            Assert.That(result, Does.Contain("couldn't save"));
        }

        [Test]
        public async Task HandleAsync_WithMonitoringService_CallsStartAndComplete()
        {
            var handlerWithMonitoring = new ReminderTaskHandler(
                _mockHistory.Object, _mockLogger.Object, _mockMonitoring.Object);

            await handlerWithMonitoring.HandleAsync("buy eggs");

            _mockMonitoring.Verify(m => m.StartRequest(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
            _mockMonitoring.Verify(m => m.CompleteRequest(
                It.IsAny<string>(), true, It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
        }

        [Test]
        public async Task HandleAsync_WhitespaceInput_ReturnsDidNotCatchMessage()
        {
            var result = await _handler.HandleAsync("   ");
            Assert.That(result, Does.Contain("didn't catch"));
        }

        [Test]
        public async Task HandleAsync_ExactlyMaxLength_SavesSuccessfully()
        {
            var exactMaxInput = new string('a', 1000);
            var result = await _handler.HandleAsync(exactMaxInput);
            _mockHistory.Verify(h => h.SaveRequestAsync(It.IsAny<RequestHistory>()), Times.Once);
        }
    }
}
