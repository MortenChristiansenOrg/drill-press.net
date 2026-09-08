namespace DrillPress.Manifest;

/// <summary>A phase duration or workload counter on the opt-in operational stderr stream.</summary>
/// <param name="Component">Process role, such as CLI, BuildHost, or rules.</param>
/// <param name="Phase">Phase or counter name.</param>
/// <param name="ProcessId">Associates repeated child invocations within one fix operation.</param>
/// <param name="WallMilliseconds">Monotonic elapsed wall time.</param>
/// <param name="UserCpuMilliseconds">User CPU consumed by this process during the phase.</param>
/// <param name="SystemCpuMilliseconds">Kernel CPU consumed by this process during the phase.</param>
/// <param name="ProcessPeakWorkingSetBytes">Process lifetime high-water mark; not a phase-specific peak.</param>
/// <param name="Count">Workload counter value, or null for a duration.</param>
public sealed record ProfileEvent(string Component, string Phase, int ProcessId, double WallMilliseconds,
    double UserCpuMilliseconds, double SystemCpuMilliseconds, long ProcessPeakWorkingSetBytes, long? Count);

internal sealed record ProcessProfileSample(TimeSpan UserCpu, TimeSpan SystemCpu, long PeakWorkingSetBytes);
