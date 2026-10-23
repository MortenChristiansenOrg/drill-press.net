using System.Diagnostics;

namespace DrillPress.Manifest;

internal class ProcessProfileProbe
{
    internal virtual ProcessProfileSample Read()
    {
        using var process = Process.GetCurrentProcess();
        return new(
            process.UserProcessorTime,
            process.PrivilegedProcessorTime,
            process.PeakWorkingSet64
        );
    }

    // Cheap enough to read around every piece of shared work, unlike the process snapshot above.
    internal virtual ProcessProfileTime Now()
    {
        var cpu = Environment.CpuUsage;
        return new(Stopwatch.GetTimestamp(), cpu.UserTime, cpu.PrivilegedTime);
    }
}
