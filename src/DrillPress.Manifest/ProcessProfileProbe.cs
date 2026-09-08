using System.Diagnostics;

namespace DrillPress.Manifest;

internal class ProcessProfileProbe
{
    internal virtual ProcessProfileSample Read()
    {
        using var process = Process.GetCurrentProcess();
        return new(process.UserProcessorTime, process.PrivilegedProcessorTime, process.PeakWorkingSet64);
    }
}
