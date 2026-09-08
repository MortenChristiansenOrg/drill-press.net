using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubProcessProfileProbe : ProcessProfileProbe
{
    internal override ProcessProfileSample Read() => new(TimeSpan.Zero, TimeSpan.Zero, 1024);
}
