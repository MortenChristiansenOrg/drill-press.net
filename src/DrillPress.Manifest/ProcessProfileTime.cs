using System.Diagnostics;

namespace DrillPress.Manifest;

// A cheap reading of the process clock and CPU time.
internal sealed record ProcessProfileTime(long Timestamp, TimeSpan UserCpu, TimeSpan SystemCpu)
{
    internal ProfileTotals Since(ProcessProfileTime start) =>
        new(
            Stopwatch.GetElapsedTime(start.Timestamp, Timestamp).TotalMilliseconds,
            (UserCpu - start.UserCpu).TotalMilliseconds,
            (SystemCpu - start.SystemCpu).TotalMilliseconds
        );
}
