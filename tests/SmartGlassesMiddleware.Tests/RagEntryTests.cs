using NUnit.Framework;
using SmartGlassesMiddleware.Models;
using System.Collections.Generic;
using System;

namespace SmartGlassesMiddleware.Tests.Models;

[TestFixture]
public class RagEntryTests
{
    [Test]
    public void DocumentChunk_Properties_Work()
    {
        var model = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = Guid.NewGuid(),
            ChunkIndex = 1,
            ChunkText = "A",
            Embedding = "[0.1]",
            EmbeddingDimension = 1,
            PageNumber = 1,
            X = 1,
            Y = 1,
            Width = 1,
            Height = 1
        };
        Assert.That(model.ChunkText, Is.EqualTo("A"));
    }
}
