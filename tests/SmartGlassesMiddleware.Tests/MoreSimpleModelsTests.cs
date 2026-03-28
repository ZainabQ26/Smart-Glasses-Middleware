using NUnit.Framework;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests.Models;

[TestFixture]
public class MoreSimpleModelsTests
{
    [Test]
    public void ChatMessage_Properties_Work()
    {
        var model = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Role = "R",
            Content = "C",
            Attachments = new List<ChatMessageAttachment>()
        };
        Assert.That(model.Role, Is.EqualTo("R"));
    }

    [Test]
    public void ChatMessageAttachment_Properties_Work()
    {
        var model = new ChatMessageAttachment
        {
            ChatMessageId = Guid.NewGuid(),
            DocumentMetadataId = Guid.NewGuid(),
        };
        Assert.That(model.ChatMessageId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void TranscriptionJob_Properties_Work()
    {
        var model = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            Status = "S",
        };
        Assert.That(model.Status, Is.EqualTo("S"));
    }
}
