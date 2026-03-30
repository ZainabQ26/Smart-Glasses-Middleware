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
    public class EditReminderTaskHandlerTests
    {
        private Mock<IHistoryService> _mockHistory = null!;
        private Mock<ILogger<EditReminderTaskHandler>> _mockLogger = null!;
        private EditReminderTaskHandler _handler = null!;

        [SetUp]
        public void Setup()
        {
            _mockHistory = new Mock<IHistoryService>();
            _mockLogger = new Mock<ILogger<EditReminderTaskHandler>>();
            _handler = new EditReminderTaskHandler(_mockHistory.Object, _mockLogger.Object);
        }

        [Test]
        public void Name_ShouldBeEditReminder()
        {
            Assert.That(_handler.Name, Is.EqualTo("edit-reminder"));
        }

        [Test]
        public void Keywords_ShouldContainEditReminder()
        {
            Assert.That(_handler.Keywords, Does.Contain("edit reminder"));
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
        public async Task HandleAsync_InputWithoutToKeyword_ReturnsInstructions()
        {
            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(new List<RequestHistory>());

            var result = await _handler.HandleAsync("change the milk reminder");
            Assert.That(result, Does.Contain("couldn't understand").Or.Contain("Try saying"));
        }

        [Test]
        public async Task HandleAsync_NoMatchingReminder_ReturnsAppropriateMessage()
        {
            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(new List<RequestHistory>());

            var result = await _handler.HandleAsync("change dentist to doctor");
            Assert.That(result, Does.Contain("couldn't find").Or.Contain("No reminder").Or.Contain("create a new"));
        }

        [Test]
        public async Task HandleAsync_MultipleMatches_ReturnsListWithoutUpdating()
        {
            var reminders = new List<RequestHistory>
            {
                new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "buy milk today", Timestamp = DateTime.UtcNow },
                new RequestHistory { Id = Guid.NewGuid(), RequestType = "reminder", InputText = "buy milk tomorrow", Timestamp = DateTime.UtcNow }
            };

            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(reminders);

            var result = await _handler.HandleAsync("change buy milk to buy bread");

            _mockHistory.Verify(h => h.UpdateReminderAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
            Assert.That(result, Does.Contain("more specific").Or.Contain("found 2").Or.Contain("2 reminder"));
        }

        [Test]
        public async Task HandleAsync_SingleMatch_CallsUpdateAndReturnsSuccess()
        {
            var reminderId = Guid.NewGuid();
            var reminders = new List<RequestHistory>
            {
                new RequestHistory { Id = reminderId, RequestType = "reminder", InputText = "buy milk", Timestamp = DateTime.UtcNow }
            };

            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(reminders);
            _mockHistory
                .Setup(h => h.UpdateReminderAsync(reminderId, "buy bread"))
                .ReturnsAsync(new RequestHistory { Id = reminderId, RequestType = "reminder", InputText = "buy bread" });

            var result = await _handler.HandleAsync("change buy milk to buy bread");

            _mockHistory.Verify(h => h.UpdateReminderAsync(reminderId, "buy bread"), Times.Once);
            Assert.That(result, Does.Contain("updated").Or.Contain("buy bread").Or.Contain("buy milk"));
        }

        [Test]
        public async Task HandleAsync_UpdateReturnsNull_ReturnsErrorMessage()
        {
            var reminderId = Guid.NewGuid();
            var reminders = new List<RequestHistory>
            {
                new RequestHistory { Id = reminderId, RequestType = "reminder", InputText = "buy milk", Timestamp = DateTime.UtcNow }
            };

            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(reminders);
            _mockHistory
                .Setup(h => h.UpdateReminderAsync(reminderId, It.IsAny<string>()))
                .ReturnsAsync((RequestHistory?)null);

            var result = await _handler.HandleAsync("change buy milk to buy bread");
            Assert.That(result, Does.Contain("couldn't update").Or.Contain("deleted"));
        }

        [Test]
        public async Task HandleAsync_ArgumentExceptionThrown_ReturnsExceptionMessage()
        {
            var reminderId = Guid.NewGuid();
            var reminders = new List<RequestHistory>
            {
                new RequestHistory { Id = reminderId, RequestType = "reminder", InputText = "buy milk", Timestamp = DateTime.UtcNow }
            };

            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(reminders);
            _mockHistory
                .Setup(h => h.UpdateReminderAsync(reminderId, It.IsAny<string>()))
                .ThrowsAsync(new ArgumentException("New text cannot be empty"));

            var result = await _handler.HandleAsync("change buy milk to buy bread");
            Assert.That(result, Does.Contain("New text cannot be empty").Or.Contain("couldn't update"));
        }

        [Test]
        public async Task HandleAsync_InvalidOperationExceptionThrown_ReturnsExceptionMessage()
        {
            var reminderId = Guid.NewGuid();
            var reminders = new List<RequestHistory>
            {
                new RequestHistory { Id = reminderId, RequestType = "reminder", InputText = "buy milk", Timestamp = DateTime.UtcNow }
            };

            _mockHistory
                .Setup(h => h.GetHistoryByTypeAsync("reminder", It.IsAny<int>()))
                .ReturnsAsync(reminders);
            _mockHistory
                .Setup(h => h.UpdateReminderAsync(reminderId, It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Reminder is locked"));

            var result = await _handler.HandleAsync("change buy milk to buy bread");
            Assert.That(result, Does.Contain("Reminder is locked").Or.Contain("couldn't update"));
        }
    }
}
