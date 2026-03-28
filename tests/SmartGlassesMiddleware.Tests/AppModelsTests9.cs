using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using System.Collections.Generic;
using System;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class AppModelsTests9
    {
        [Test]
        public void AdditionalModelsCoverage2()
        {
            var req = new ChatMessageAttachment();
            Assert.That(req.ChatMessageId, Is.EqualTo(Guid.Empty));
            Assert.That(req.DocumentMetadataId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void AdditionalModelsCoverage3()
        {
            var req = new DocumentGroup();
            Assert.That(req.DocumentMetadataId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void AdditionalModelsCoverage4()
        {
            var req = new DocumentChunk();
            Assert.That(req.ChunkIndex, Is.EqualTo(0));
            Assert.That(req.Embedding, Is.Null);
        }
    }
}
