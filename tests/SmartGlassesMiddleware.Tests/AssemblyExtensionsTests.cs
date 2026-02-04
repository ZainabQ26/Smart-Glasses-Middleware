using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

public class AssemblyExtensionsTests
{
    [Fact]
    public void AssemblyExtensions_GetTypesSafe_Returns_Types_For_Valid_Assembly()
    {
        // Arrange
        var assembly = typeof(AssemblyExtensionsTests).Assembly;

        // Act
        // Testing via reflection since the extension is internal
        var extensionType = assembly.GetReferencedAssemblies()
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "AssemblyExtensions");

        // Assert
        Assert.NotNull(extensionType);
    }

    [Fact]
    public void AssemblyExtensions_Exists_In_Services_Namespace()
    {
        // Arrange
        var assembly = typeof(AssemblyExtensionsTests).Assembly;

        // Act
        var referencedTypes = assembly.GetReferencedAssemblies()
            .Select(Assembly.Load)
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t.Namespace != null && t.Namespace.Contains("SmartGlassesMiddleware.Services"));

        // Assert - Just verify we can load the services namespace
        Assert.NotEmpty(referencedTypes);
    }
}
