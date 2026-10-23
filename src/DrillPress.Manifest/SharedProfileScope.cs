namespace DrillPress.Manifest;

// One piece of shared work, added to its profile's shared totals when disposed.
internal sealed class SharedProfileScope : IDisposable
{
    internal static readonly SharedProfileScope None = new(null, "", null);

    private readonly PipelineProfile? _profile;
    private readonly string _kind;
    private readonly ProcessProfileTime? _start;
    private bool _disposed;

    internal SharedProfileScope(PipelineProfile? profile, string kind, ProcessProfileTime? start)
    {
        _profile = profile;
        _kind = kind;
        _start = start;
    }

    public void Dispose()
    {
        if (_disposed || _profile is null || _start is null)
            return;
        _disposed = true;
        _profile.EndShared(_kind, _start);
    }
}
