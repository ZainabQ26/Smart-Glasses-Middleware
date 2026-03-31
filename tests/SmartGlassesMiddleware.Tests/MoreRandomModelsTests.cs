using NUnit.Framework;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests.Models;

[TestFixture]
public class MoreRandomModelsTests
{
    [Test]
    public void DocumentSource_Properties_Work()
    {
        var model = new DocumentSource
        {
            DocumentId = Guid.NewGuid(),
            FileName = "FN",
            PageNumber = 1,
            ChunkId = Guid.NewGuid(),
            ChunkText = "CT",
            X = 1.0,
            Y = 2.0,
            Width = 3.0,
            Height = 4.0,
            Position = "bottom right"
        };
        Assert.That(model.FileName, Is.EqualTo("FN"));
    }

    [Test]
    public void ConversationSource_Properties_Work()
    {
        var model = new ConversationSource
        {
            ConversationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            Role = "R",
            Content = "C",
            Timestamp = DateTime.UtcNow
        };
        Assert.That(model.Role, Is.EqualTo("R"));
    }

    [Test]
    public void RequestHistory_Properties_Work()
    {
        var model = new RequestHistory
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            RequestType = "RT",
            InputText = "IT",
            DetectedTask = "DT",
            Response = "R",
            UsedAdvancedModel = true,
            MediaStorageKey = "MSK",
            CorrelationId = "CID"
        };
        Assert.That(model.RequestType, Is.EqualTo("RT"));
    }
}
