using System.Diagnostics;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

// A process clock that moves only when a test advances it; wall and user CPU advance together.
internal sealed class ManualProcessProfileProbe : ProcessProfileProbe
{
    private TimeSpan _elapsed;

    internal void Advance(int milliseconds) => _elapsed += TimeSpan.FromMilliseconds(milliseconds);

    internal override ProcessProfileSample Read() => new(_elapsed, TimeSpan.Zero, 1024);

    internal override ProcessProfileTime Now() =>
        new((long)(_elapsed.TotalSeconds * Stopwatch.Frequency), _elapsed, TimeSpan.Zero);
}
