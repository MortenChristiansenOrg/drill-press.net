using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Writes opt-in process phase measurements without mixing them with diagnostic stdout.</summary>
public sealed class PipelineProfile
{
    private readonly TextWriter _output;
    private readonly string _component;
    private readonly ProcessProfileProbe _probe;

    /// <summary>Creates a per-invocation profile; disabled profiles produce no output or process probes.</summary>
    public PipelineProfile(bool enabled, TextWriter output, string component)
        : this(enabled, output, component, new ProcessProfileProbe()) { }

    internal PipelineProfile(bool enabled, TextWriter output, string component, ProcessProfileProbe probe)
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

    internal void Fail(Exception exception) => Failure ??= exception.Message;

    /// <summary>Starts a wall/CPU measurement which is emitted when the returned scope is disposed.</summary>
    public ProfileMeasurement Measure(string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        return new(this, phase);
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
        if (!CanRecord) return;
        try
        {
            _output.WriteLine("drillpress-profile " + JsonSerializer.Serialize(
                measurement with { Component = _component }, CompilationSnapshotJsonContext.Default.ProfileEvent));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }
}
