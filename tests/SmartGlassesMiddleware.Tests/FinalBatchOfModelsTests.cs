using NUnit.Framework;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests.Models;

[TestFixture]
public class FinalBatchOfModelsTests
{
    [Test]
    public void GroupConversationMapping_Mapping_Works()
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
