using System.Reflection;

namespace SmartGlassesMiddleware.Services;

internal static class AssemblyExtensions
{
    public static IEnumerable<Type> GetTypesSafe(this Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }
}
