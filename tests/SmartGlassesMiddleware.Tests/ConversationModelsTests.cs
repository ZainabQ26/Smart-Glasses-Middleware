using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using System.Collections.Generic;
using System;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class ConversationModelsTests
    {
        [Test]
        public void Conversation_Properties()
        {
            var convo = new Conversation { Title = "t" };
            Assert.That(convo.Title, Is.EqualTo("t"));
            Assert.That(convo.Messages, Is.Not.Null);
        }

        [Test]
        public void ChatMessage_Properties()
        {
            var msg = new ChatMessage { Role = "user", Content = "hi" };
            Assert.That(msg.Role, Is.EqualTo("user"));
            Assert.That(msg.Content, Is.EqualTo("hi"));
            Assert.That(msg.Attachments, Is.Not.Null);
        }

        [Test]
        public void GroupConversationMapping_Properties()
        {
            var mapping = new GroupConversationMapping { ResourceGroupId = Guid.NewGuid() };
            Assert.That(mapping.ResourceGroupId, Is.Not.EqualTo(Guid.Empty));
        }

        [Test]
        public void RequestHistory_Properties()
        {
            var req = new RequestHistory { RequestType = "test", InputText = "input" };
            Assert.That(req.RequestType, Is.EqualTo("test"));
            Assert.That(req.InputText, Is.EqualTo("input"));
        }

        [Test]
        public void ManagedFile_Properties()
        {
            var file = new ManagedFile { FileName = "f1", ContentType = "image/png", StorageKey = "k1", FileSize = 1024 };
            Assert.That(file.FileName, Is.EqualTo("f1"));
            Assert.That(file.ContentType, Is.EqualTo("image/png"));
            Assert.That(file.StorageKey, Is.EqualTo("k1"));
            Assert.That(file.FileSize, Is.EqualTo(1024));
        }

        [Test]
        public void DocumentMetadata_Properties()
        {
            var file = new DocumentMetadata { FileName = "f1", ContentType = "image/png", StorageKey = "k1", FileSize = 1024, IsProcessed = true };
            Assert.That(file.FileName, Is.EqualTo("f1"));
            Assert.That(file.ContentType, Is.EqualTo("image/png"));
            Assert.That(file.StorageKey, Is.EqualTo("k1"));
            Assert.That(file.FileSize, Is.EqualTo(1024));
            Assert.That(file.IsProcessed, Is.True);
        }
    }
}
