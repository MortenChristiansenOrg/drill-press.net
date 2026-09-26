using System.Reflection;

namespace DrillPress.Manifest;

internal static class ComponentVersion
{
    internal static string Current { get; } =
        typeof(ComponentVersion)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

    internal static void RequireMatch(string version, string component)
    {
        if (version != Current)
        {
            throw new InvalidDataException(
                $"{component} version '{version}' is incompatible with Drill Press {Current}. Install tool and SDK packages at {Current} and rebuild the rule bundle. Releases require exact version matching."
            );
        }
    }
}
