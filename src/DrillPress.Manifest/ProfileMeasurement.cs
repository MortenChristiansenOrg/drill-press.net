using System.Diagnostics;

namespace DrillPress.Manifest;

/// <summary>Measures one scope; CPU and peak working set describe this process, excluding child processes.</summary>
public sealed class ProfileMeasurement : IDisposable
{
    private readonly PipelineProfile _profile;
    private readonly string _phase;
    private readonly ProcessProfileSample? _initial;
    private readonly long _start;
    private bool _disposed;

    internal ProfileMeasurement(PipelineProfile profile, string phase)
    {
        _profile = profile;
        _phase = phase;
        if (profile.CanRecord)
        {
            try
            {
                _initial = profile.ReadProcess();
                _start = Stopwatch.GetTimestamp();
            }
            catch (Exception exception)
            {
                profile.Fail(exception);
            }
        }
    }

    /// <summary>Emits the measurement once, including when a scope exits through an exception.</summary>
    public void Dispose()
    {
        if (_disposed || _initial is null || !_profile.CanRecord)
        {
            return;
        }

        _disposed = true;
        var wall = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        try
        {
            var current = _profile.ReadProcess();
            _profile.Write(
                new(
                    "",
                    _phase,
                    Environment.ProcessId,
                    wall,
                    (current.UserCpu - _initial.UserCpu).TotalMilliseconds,
                    (current.SystemCpu - _initial.SystemCpu).TotalMilliseconds,
                    current.PeakWorkingSetBytes,
                    null
                )
            );
        }
        catch (Exception exception)
        {
            _profile.Fail(exception);
        }
    }
}
