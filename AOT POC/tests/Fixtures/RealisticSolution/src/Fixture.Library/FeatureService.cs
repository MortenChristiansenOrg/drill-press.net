using Humanizer;

namespace Fixture.Library;

public sealed class FeatureService : ILinkedContract
{
    public string Describe(int value)
    {
#if DRILLPRESS_FEATURE
        return $"{value.ToWords()}:{GeneratedMarker.Value}";
#else
#error DRILLPRESS_FEATURE was not supplied by evaluated MSBuild properties.
#endif
    }
}

internal sealed class CompilerSeverityFixture(string intentionallyUnused)
{
}
