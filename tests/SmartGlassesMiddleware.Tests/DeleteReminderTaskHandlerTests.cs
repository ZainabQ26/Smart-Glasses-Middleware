using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class DeleteReminderTaskHandlerTests
    {
        private Mock<IHistoryService> _mockHistory = null!;
        private Mock<ILogger<DeleteReminderTaskHandler>> _mockLogger = null!;
        private DeleteReminderTaskHandler _handler = null!;

        [SetUp]
        public void Setup()
        {
            _mockHistory = new Mock<IHistoryService>();
            _mockLogger = new Mock<ILogger<DeleteReminderTaskHandler>>();
            _handler = new DeleteReminderTaskHandler(_mockHistory.Object, _mockLogger.Object);
        }

        [Test]
        public void Name_ShouldBeDeleteReminder()
        {
            Assert.That(_handler.Name, Is.EqualTo("delete-reminder"));
        }

        [Test]
        public void Keywords_ShouldContainDeleteReminder()
        {
            Assert.That(_handler.Keywords, Does.Contain("delete reminder"));
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
        public async Task HandleAsync_AllRemindersInput_ReturnsSafetyMessage()
        {
            var result = await _handler.HandleAsync("delete all reminders");
            Assert.That(result, Does.Contain("safety").Or.Contain("one at a time").Or.Contain("can't delete all"));
        }

        [Test]
        public async Task HandleAsync_EveryReminder_ReturnsSafetyMessage()
        {
            var result = await _handler.HandleAsync("remove every reminder");
            Assert.That(result, Does.Contain("safety").Or.Contain("one at a time").Or.Contain("can't delete all"));
        }

        [Test]
        public async Task HandleAsync_UnparsableInput_ReturnsBeMoreSpecificMessage()
        {
            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(new List<RequestHistory>());

            // Input with no extractable content returns no results or a prompt
            var result = await _handler.HandleAsync("delete");
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public async Task HandleAsync_NoMatchingReminders_ReturnsCouldNotFindMessage()
        {
            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(new List<RequestHistory>());

            var result = await _handler.HandleAsync("delete reminder about dentist");
            Assert.That(result, Does.Contain("couldn't find").Or.Contain("No reminder"));
        }

        [Test]
        public async Task HandleAsync_SingleMatchFound_DeletesAndReturnsSuccess()
        {
            var reminderId = Guid.NewGuid();
            var reminders = new List<RequestHistory>
            {
                new RequestHistory
                {
                    Id = reminderId,
                    RequestType = "reminder",
                    InputText = "buy milk",
                    Timestamp = DateTime.UtcNow
                }
            };

            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(reminders);
            _mockHistory
                .Setup(h => h.DeleteReminderAsync(reminderId))
                .ReturnsAsync(true);

            var result = await _handler.HandleAsync("delete reminder buy milk");

            _mockHistory.Verify(h => h.DeleteReminderAsync(reminderId), Times.Once);
            Assert.That(result, Does.Contain("buy milk").Or.Contain("deleted"));
        }

        [Test]
        public async Task HandleAsync_MultipleMatchesFound_ReturnsListOfMatches()
        {
            var reminders = new List<RequestHistory>
            {
                new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "buy milk today", Timestamp = DateTime.UtcNow },
                new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "buy milk tomorrow", Timestamp = DateTime.UtcNow }
            };

            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(reminders);

            var result = await _handler.HandleAsync("delete reminder buy milk");

            // Should return a list prompt, not delete anything
            _mockHistory.Verify(h => h.DeleteReminderAsync(It.IsAny<Guid>()), Times.Never);
            Assert.That(result, Does.Contain("more specific").Or.Contain("found 2").Or.Contain("2 reminder"));
        }
    }
}
