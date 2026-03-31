using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class ControllerModelsTests
{
    [Test]
    public void UpdateReminderRequest_Properties_Work()
    {
        var model = new UpdateReminderRequest { Text = "Test" };
        Assert.That(model.Text, Is.EqualTo("Test"));
    }

    [Test]
    public void CreateConversationRequest_Properties_Work()
    {
        var model = new CreateConversationRequest { Title = "T", ResourceGroupId = Guid.NewGuid() };
        Assert.That(model.Title, Is.EqualTo("T"));
    }

    [Test]
    public void AddMessageRequest_Properties_Work()
    {
        var model = new AddMessageRequest { Role = "R", Content = "C", Model = "M", DocumentIds = new List<Guid>() };
        Assert.That(model.Role, Is.EqualTo("R"));
    }

    [Test]
    public void ConversationQueryRequest_Properties_Work()
    {
        var model = new ConversationQueryRequest { Query = "Q", TopK = 5, GroupIds = new List<Guid>(), Model = "M" };
        Assert.That(model.Query, Is.EqualTo("Q"));
    }

    [Test]
    public void UpdateConversationRequest_Properties_Work()
    {
        var model = new UpdateConversationRequest { Title = "T" };
        Assert.That(model.Title, Is.EqualTo("T"));
    }

    [Test]
    public void UpdateConversationGroupRequest_Properties_Work()
    {
        var model = new UpdateConversationGroupRequest { ResourceGroupId = Guid.NewGuid() };
        Assert.That(model.ResourceGroupId, Is.Not.Null);
    }

    [Test]
    public void ProcessRequest_Properties_Work()
    {
        var model = new ProcessRequest { Input = "I", DocumentContext = null };
        Assert.That(model.Input, Is.EqualTo("I"));
    }

    [Test]
    public void PageSummaryRequest_Properties_Work()
    {
        var model = new PageSummaryRequest { PageText = "PT", PageNumber = 1, DocumentName = "DN" };
        Assert.That(model.PageText, Is.EqualTo("PT"));
    }

    [Test]
    public void QueryRequest_Properties_Work()
    {
        var model = new QueryRequest { Query = "Q", TopK = 5, GroupIds = new List<Guid>(), DocumentIds = new List<Guid>(), ConversationId = Guid.NewGuid(), RequestId = "R" };
        Assert.That(model.Query, Is.EqualTo("Q"));
    }

    [Test]
    public void MoveDocumentRequest_Properties_Work()
    {
        var model = new MoveDocumentRequest { GroupIds = new List<Guid>(), RegenerateEmbeddings = true };
        Assert.That(model.RegenerateEmbeddings, Is.True);
    }

    [Test]
    public void HighlightRequest_Properties_Work()
    {
        var model = new HighlightRequest { ChunkIds = new List<Guid>(), SearchText = "ST" };
        Assert.That(model.SearchText, Is.EqualTo("ST"));
    }
}
