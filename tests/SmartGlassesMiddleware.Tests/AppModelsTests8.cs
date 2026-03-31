using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using System.Collections.Generic;
using System;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class AppModelsTests8
    {
        [Test]
        public void AIInteractionLog_Properties()
        {
            var id = Guid.NewGuid();
            var dt = DateTime.UtcNow;
            var req = new AIInteractionLog { Id = id, InputTokens = 1, OutputTokens = 2, TotalTokens = 3, ModelUsed = "m", ResponseTimeMs = 4, Timestamp = dt, Input = "i" };
            Assert.That(req.Id, Is.EqualTo(id));
            Assert.That(req.InputTokens, Is.EqualTo(1));
            Assert.That(req.OutputTokens, Is.EqualTo(2));
            Assert.That(req.TotalTokens, Is.EqualTo(3));
            Assert.That(req.ModelUsed, Is.EqualTo("m"));
            Assert.That(req.ResponseTimeMs, Is.EqualTo(4));
            Assert.That(req.Timestamp, Is.EqualTo(dt));
            Assert.That(req.Input, Is.EqualTo("i"));
        }

        [Test]
        public void GroupConversationMapping_Properties()
        {
            var id = Guid.NewGuid();
            var test1 = new GroupConversationMapping { Id = id, ConversationId = Guid.NewGuid(), ResourceGroupId = Guid.NewGuid() };
            Assert.That(test1.Id, Is.EqualTo(id));
            Assert.That(test1.ConversationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(test1.ResourceGroupId, Is.Not.EqualTo(Guid.Empty));
        }

        [Test]
        public void ChatMessage_Properties_More()
        {
            var test1 = new ChatMessage { Model = "test", TokenCount = 2, Timestamp = DateTime.MinValue, Id = Guid.NewGuid(), ConversationId = Guid.NewGuid(), Conversation = new Conversation() };
            Assert.That(test1.Model, Is.EqualTo("test"));
            Assert.That(test1.TokenCount, Is.EqualTo(2));
            Assert.That(test1.Timestamp, Is.EqualTo(DateTime.MinValue));
            Assert.That(test1.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(test1.ConversationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(test1.Conversation, Is.Not.Null);
        }

        [Test]
        public void ChatMessageAttachment_Properties_More()
        {
            var test1 = new ChatMessageAttachment { ChatMessage = new ChatMessage(), Document = new DocumentMetadata() };
            Assert.That(test1.ChatMessage, Is.Not.Null);
            Assert.That(test1.Document, Is.Not.Null);
        }

        [Test]
        public void Conversation_Properties_More()
        {
            var test1 = new Conversation { Id = Guid.NewGuid(), CreatedAt = DateTime.MinValue, UpdatedAt = DateTime.MaxValue, IsActive = false, ResourceGroupId = Guid.NewGuid(), ResourceGroup = new ResourceGroup(), Summary = "s", SummaryEmbedding = "e", SummaryEmbeddingDimension = 3 };
            Assert.That(test1.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(test1.CreatedAt, Is.EqualTo(DateTime.MinValue));
            Assert.That(test1.UpdatedAt, Is.EqualTo(DateTime.MaxValue));
            Assert.That(test1.IsActive, Is.False);
            Assert.That(test1.ResourceGroupId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(test1.ResourceGroup, Is.Not.Null);
            Assert.That(test1.Summary, Is.EqualTo("s"));
            Assert.That(test1.SummaryEmbedding, Is.EqualTo("e"));
            Assert.That(test1.SummaryEmbeddingDimension, Is.EqualTo(3));
        }

        [Test]
        public void RequestHistory_Properties_More()
        {
            var test1 = new RequestHistory { Id = Guid.NewGuid(), Timestamp = DateTime.MinValue, DetectedTask = "d", Response = "r", UsedAdvancedModel = true, MediaStorageKey = "m", CorrelationId = "c" };
            Assert.That(test1.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(test1.Timestamp, Is.EqualTo(DateTime.MinValue));
            Assert.That(test1.DetectedTask, Is.EqualTo("d"));
            Assert.That(test1.Response, Is.EqualTo("r"));
            Assert.That(test1.UsedAdvancedModel, Is.True);
            Assert.That(test1.MediaStorageKey, Is.EqualTo("m"));
            Assert.That(test1.CorrelationId, Is.EqualTo("c"));
        }

        [Test]
        public void TaskRequest_Properties_More()
        {
            var test1 = new TaskRequest { Text = "t" };
            Assert.That(test1.Text, Is.EqualTo("t"));
        }

        [Test]
        public void TaskResponse_Properties_More()
        {
            var test1 = new TaskResponse { Response = "t", Task = "ts", UsedAdvancedModel = true, RequestId = Guid.NewGuid() };
            Assert.That(test1.Response, Is.EqualTo("t"));
            Assert.That(test1.Task, Is.EqualTo("ts"));
            Assert.That(test1.UsedAdvancedModel, Is.True);
            Assert.That(test1.RequestId, Is.Not.EqualTo(Guid.Empty));
        }
    }
}
