using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Writes opt-in process phase measurements without mixing them with diagnostic stdout.</summary>
public sealed class PipelineProfile
{
    private readonly TextWriter _output;
    private readonly string _component;
    private readonly ProcessProfileProbe _probe;

    // Set while the current flow, including tasks it starts, is inside shared work, so nested shared work counts once.
    private static readonly AsyncLocal<bool> _insideShared = new();
    private readonly SortedDictionary<string, ProfileTotals> _shared = new(StringComparer.Ordinal);
    private ProfileTotals _sharedTotal;

    /// <summary>Creates a per-invocation profile; disabled profiles produce no output or process probes.</summary>
    public PipelineProfile(bool enabled, TextWriter output, string component)
        : this(enabled, output, component, new ProcessProfileProbe()) { }

    internal PipelineProfile(
        bool enabled,
        TextWriter output,
        string component,
        ProcessProfileProbe probe
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        Enabled = enabled;
        _output = output;
        _component = component;
        _probe = probe;
    }

    /// <summary>Whether this invocation records measurements.</summary>
    public bool Enabled { get; }

    /// <summary>The first unavailable measurement or output error; profiling failures never interrupt source recovery.</summary>
    public string? Failure { get; private set; }

    internal bool CanRecord => Enabled && Failure is null;

    internal ProcessProfileSample ReadProcess() => _probe.Read();

    internal ProcessProfileTime Now() => _probe.Now();

    internal void Fail(Exception exception) => Failure ??= exception.Message;

    /// <summary>Starts a wall/CPU measurement which is emitted when the returned scope is disposed.</summary>
    public ProfileMeasurement Measure(string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        return new(this, phase);
    }

    // Also emits ownPhase: the same scope without the shared work it paid for.
    internal ProfileMeasurement Measure(string phase, string ownPhase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownPhase);
        return new(this, phase, ownPhase);
    }

    // Measures cached work that later consumers reuse, such as binding a document once for every rule.
    // Only the outermost shared scope of a flow counts; work it starts on other threads is part of it.
    internal SharedProfileScope MeasureShared(string kind)
    {
        if (!CanRecord || _insideShared.Value)
            return SharedProfileScope.None;
        try
        {
            var start = Now();
            _insideShared.Value = true;
            return new(this, kind, start);
        }
        catch (Exception exception)
        {
            Fail(exception);
            return SharedProfileScope.None;
        }
    }

    internal ProfileTotals SharedTotal
    {
        get
        {
            lock (_shared)
                return _sharedTotal;
        }
    }

    internal void EndShared(string kind, ProcessProfileTime start)
    {
        _insideShared.Value = false;
        try
        {
            var spent = Now().Since(start);
            lock (_shared)
            {
                _shared[kind] = _shared.GetValueOrDefault(kind) + spent;
                _sharedTotal += spent;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    // Emits the shared work since the previous report, one shared.<kind> phase per kind in name order.
    internal void WriteShared()
    {
        KeyValuePair<string, ProfileTotals>[] totals;
        lock (_shared)
        {
            totals = [.. _shared];
            _shared.Clear();
        }
        if (totals.Length == 0 || !CanRecord)
            return;
        try
        {
            var peak = ReadProcess().PeakWorkingSetBytes;
            foreach (var (kind, total) in totals)
                Write(
                    new(
                        "",
                        "shared." + kind,
                        Environment.ProcessId,
                        total.Wall,
                        total.UserCpu,
                        total.SystemCpu,
                        peak,
                        null
                    )
                );
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    /// <summary>Emits a deterministic workload counter alongside phase measurements.</summary>
    public void Count(string name, long value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (CanRecord)
        {
            Write(new(_component, name, Environment.ProcessId, 0, 0, 0, 0, value));
        }
    }

    internal void Write(ProfileEvent measurement)
    {
        if (!CanRecord)
            return;
        try
        {
            _output.WriteLine(
                "drillpress-profile "
                    + JsonSerializer.Serialize(
                        measurement with
                        {
                            Component = _component,
                        },
                        CompilationSnapshotJsonContext.Default.ProfileEvent
                    )
            );
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }
}
