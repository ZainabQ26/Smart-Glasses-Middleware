using NUnit.Framework;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests.Models;

[TestFixture]
public class FinalBatchOfModelsTestsTwo
{
    [Test]
    public void DocumentGroup_Properties_Work()
    {
        var model = new DocumentGroup
        {
            DocumentMetadataId = Guid.NewGuid(),
            ResourceGroupId = Guid.NewGuid()
        };
        Assert.That(model.DocumentMetadataId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void ManagedFileGroup_Properties_Work()
    {
        var model = new ManagedFileGroup
        {
            ManagedFileId = Guid.NewGuid(),
            ResourceGroupId = Guid.NewGuid()
        };
        Assert.That(model.ManagedFileId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void ManagedPromptGroup_Properties_Work()
    {
        var model = new ManagedPromptGroup
        {
            ManagedPromptId = Guid.NewGuid(),
            ResourceGroupId = Guid.NewGuid()
        };
        Assert.That(model.ManagedPromptId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void RequestStatus_Properties_Work()
    {
        var model = new RequestStatus
        {
            Id = Guid.NewGuid(),
            RequestType = "ReqType",
            Status = "S",
            Result = "Res"
        };
        Assert.That(model.Status, Is.EqualTo("S"));
    }
}
