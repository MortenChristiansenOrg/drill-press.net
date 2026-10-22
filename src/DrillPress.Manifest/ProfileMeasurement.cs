namespace DrillPress.Manifest;

/// <summary>Measures one scope; CPU and peak working set describe this process, excluding child processes.</summary>
public sealed class ProfileMeasurement : IDisposable
{
    private readonly PipelineProfile _profile;
    private readonly string _phase;
    private readonly string? _ownPhase;
    private readonly ProcessProfileTime? _start;
    private readonly ProfileTotals _sharedAtStart;
    private bool _disposed;

    internal ProfileMeasurement(PipelineProfile profile, string phase, string? ownPhase = null)
    {
        _profile = profile;
        _phase = phase;
        _ownPhase = ownPhase;
        if (profile.CanRecord)
        {
            try
            {
                _sharedAtStart = profile.SharedTotal;
                _start = profile.Now();
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
        if (_disposed || _start is null || !_profile.CanRecord)
        {
            return;
        }

        _disposed = true;
        try
        {
            var spent = _profile.Now().Since(_start);
            var peak = _profile.ReadProcess().PeakWorkingSetBytes;
            Write(_phase, spent, peak);
            if (_ownPhase is not null)
                Write(_ownPhase, spent - (_profile.SharedTotal - _sharedAtStart), peak);
        }
        catch (Exception exception)
        {
            _profile.Fail(exception);
        }
    }

    private void Write(string phase, ProfileTotals spent, long peak) =>
        _profile.Write(
            new(
                "",
                phase,
                Environment.ProcessId,
                spent.Wall,
                spent.UserCpu,
                spent.SystemCpu,
                peak,
                null
            )
        );
}
