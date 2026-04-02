using NUnit.Framework;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests.Models;

[TestFixture]
public class MoreModelsTests
{
    [Test]
    public void ManagedPrompt_Properties_Work()
    {
        var model = new ManagedPrompt
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            PromptText = "Text",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Assert.That(model.Name, Is.EqualTo("Test"));
        Assert.That(model.PromptText, Is.EqualTo("Text"));
    }

    [Test]
    public void Conversation_Properties_Work()
    {
        var model = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Title",
            ResourceGroupId = Guid.NewGuid(),
            Summary = "Summary",
            SummaryEmbedding = "[0.1]",
            SummaryEmbeddingDimension = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Messages = new List<ChatMessage>()
        };
        Assert.That(model.Title, Is.EqualTo("Title"));
        Assert.That(model.Summary, Is.EqualTo("Summary"));
    }

    [Test]
    public void ResourceGroup_Properties_Work()
    {
        var model = new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Name",
            Description = "Desc",
            CreatedAt = DateTime.UtcNow,
            DocumentGroups = new List<DocumentGroup>(),
            Conversations = new List<Conversation>()
        };
        Assert.That(model.Name, Is.EqualTo("Name"));
    }

    [Test]
    public void Abbreviation_Properties_Work()
    {
        var model = new Abbreviation
        {
            Id = Guid.NewGuid(),
            ShortForm = "SF",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Assert.That(model.ShortForm, Is.EqualTo("SF"));
    }

    [Test]
    public void ManagedFile_Properties_Work()
    {
        var model = new ManagedFile
        {
            Id = Guid.NewGuid(),
            FileName = "FN",
            ContentType = "CT",
            StorageKey = "SK",
            ContentHash = "CH",
            FileSize = 1,
            Label = "L",
            UploadedAt = DateTime.UtcNow
        };
        Assert.That(model.FileName, Is.EqualTo("FN"));
    }

    [Test]
    public void DocumentChunk_Properties_Work()
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = Guid.NewGuid(),
            ChunkIndex = 1,
            ChunkText = "Text",
            Embedding = "[0.1]",
            EmbeddingDimension = 1,
            PageNumber = 1,
            X = 1,
            Y = 1,
            Width = 1,
            Height = 1
        };
        Assert.That(chunk.ChunkText, Is.EqualTo("Text"));
    }
}
