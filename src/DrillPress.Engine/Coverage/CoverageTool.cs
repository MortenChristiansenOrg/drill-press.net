using System.Reflection;

namespace DrillPress.Engine;

internal static class CoverageTool
{
    internal static string Version =>
        typeof(CoverageTool)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "DrillPressCoverageVersion")
            .Value!;
}
