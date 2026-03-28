using NUnit.Framework;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests.Models;

[TestFixture]
public class EvenMoreModelsTests
{
    [Test]
    public void DocumentLocationModels_Properties_Work()
    {
        var tl = new TextLocation
        {
            DocumentId = Guid.NewGuid(),
            DocumentName = "DN",
            ChunkId = Guid.NewGuid(),
            PageNumber = 1,
            X = 1,
            Y = 1,
            Width = 1,
            Height = 1,
            Position = "P",
            ContextText = "CT"
        };
        var res = new FindLocationResponse
        {
            DocumentId = Guid.NewGuid(),
            DocumentName = "DN",
            SearchText = "ST",
            MatchCount = 1,
            Locations = new List<TextLocation> { tl }
        };
        var req = new FindLocationRequest
        {
            SearchText = "ST"
        };
        Assert.That(tl.DocumentName, Is.EqualTo("DN"));
        Assert.That(res.SearchText, Is.EqualTo("ST"));
        Assert.That(req.SearchText, Is.EqualTo("ST"));
    }

    [Test]
    public void GroupConversationMapping_Properties_Work()
    {
        var model = new GroupConversationMapping
        {
            ResourceGroupId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            UpdatedAt = DateTime.UtcNow
        };
        Assert.That(model.ResourceGroupId, Is.Not.EqualTo(Guid.Empty));
    }
}
