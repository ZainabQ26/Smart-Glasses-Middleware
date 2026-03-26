using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests
{
    public class ConversationServicePerformanceTests
    {
        private ApplicationDbContext _context = null!;
        private ConversationService _service = null!;

        [SetUp]
        public void Setup()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);

            var cacheMock = new Mock<ICacheService>();

            _service = new ConversationService(
                _context,
                new NullLogger<ConversationService>(),
                configMock.Object,
                new Mock<IServiceProvider>().Object,
                cacheMock.Object
            );
        }

        [TearDown]
        public void TearDown()
        {
            _context.Dispose();
        }

        [Test]
        public async Task BenchmarkSplitConversation()
        {
            // Arrange
            var originalConversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Title = "Test Original",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };
            _context.Conversations.Add(originalConversation);
            await _context.SaveChangesAsync();

            int messageCount = 1000;
            var messages = new List<ChatMessage>();
            for (int i = 0; i < messageCount; i++)
            {
                messages.Add(new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = originalConversation.Id,
                    Role = "user",
                    Content = $"Message {i}",
                    Timestamp = DateTime.UtcNow
                });
            }
            _context.ChatMessages.AddRange(messages);
            await _context.SaveChangesAsync();

            // Act
            var stopwatch = Stopwatch.StartNew();

            await _service.SplitConversationAsync(
                originalConversation.Id,
                "New Topic",
                messages // Move all messages
            );

            stopwatch.Stop();

            Console.WriteLine($"SplitConversationAsync took {stopwatch.ElapsedMilliseconds} ms to move {messageCount} messages.");
        }
    }
}
