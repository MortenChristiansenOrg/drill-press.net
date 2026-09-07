using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DrillPress.Benchmarks;

public static class ChildPeakMemory
{
    // The original process handle is still open, including after Windows exit.
    public static long Read(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Process working-set counters require Windows.");
        }

        if (!GetProcessMemoryInfo(process.SafeHandle, out var counters,
                (uint)Marshal.SizeOf<ProcessMemoryCounters>()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var bytes = checked((long)counters._peakWorkingSetSize);
        return bytes > 0 ? bytes : throw new InvalidOperationException("Child peak memory was unavailable.");
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        SafeProcessHandle process, out ProcessMemoryCounters counters, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint _size, _pageFaultCount;
        public nuint _peakWorkingSetSize, _workingSetSize, _quotaPeakPagedPoolUsage, _quotaPagedPoolUsage;
        public nuint _quotaPeakNonPagedPoolUsage, _quotaNonPagedPoolUsage, _pagefileUsage, _peakPagefileUsage;
    }
}
