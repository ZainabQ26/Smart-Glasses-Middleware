using NUnit.Framework;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// ServerSettingsService was removed: prevent-sleep and groq runtime toggle are no longer needed.
/// Groq is always enabled; the server never sleeps.
/// </summary>
[TestFixture]
public class ServerSettingsServiceTests
{
    [Test]
    public void Placeholder_ServiceRemoved()
    {
        // ServerSettingsService has been removed. This placeholder keeps the test file
        // present so the project compiles without changes to the .csproj includes.
        Assert.Pass();
    }
}
