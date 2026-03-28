using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using System.Linq;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class AppDbContextTests
    {
        [Test]
        public void DbContext_Properties_Work()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "AppDbTest")
                .Options;

            using var context = new ApplicationDbContext(options);

            Assert.That(context.Conversations, Is.Not.Null);
            Assert.That(context.ChatMessages, Is.Not.Null);
            Assert.That(context.RequestHistories, Is.Not.Null);
            Assert.That(context.DocumentMetadatas, Is.Not.Null);
            Assert.That(context.DocumentChunks, Is.Not.Null);
            Assert.That(context.RagEntries, Is.Not.Null);
            Assert.That(context.AIInteractionLogs, Is.Not.Null);
            Assert.That(context.TranscriptionJobs, Is.Not.Null);
            Assert.That(context.Abbreviations, Is.Not.Null);
            Assert.That(context.RequestStatuses, Is.Not.Null);
            Assert.That(context.ManagedFiles, Is.Not.Null);
            Assert.That(context.ManagedPrompts, Is.Not.Null);
            Assert.That(context.ResourceGroups, Is.Not.Null);
            Assert.That(context.DocumentGroups, Is.Not.Null);
            Assert.That(context.GroupConversationMappings, Is.Not.Null);
            Assert.That(context.ChatMessageAttachments, Is.Not.Null);
        }
    }
}
