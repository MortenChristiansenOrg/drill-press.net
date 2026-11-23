using DrillPress.Manifest;

namespace DrillPress;

internal sealed class RewriteValidationCache(AnalysisProject project)
{
    private readonly object _gate = new();
    private readonly Lazy<CompilationErrors> _original = new(() =>
        CompilationErrors.Read(project, project.Compilation)
    );
    private SourceEdit[]? _edits;
    private PreparedRewrite _prepared;

    internal CompilationErrors OriginalErrors => _original.Value;

    internal PreparedRewrite Get(
        IReadOnlyList<SourceEdit> edits,
        Func<SourceEdit[], PreparedRewrite> create
    )
    {
        project.CancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_edits is not null && _edits.SequenceEqual(edits))
                return _prepared;
            var snapshot = edits.ToArray();
            var prepared = create(snapshot);
            // Bound retention to one compilation, and compare edit values rather than mutable list identity.
            _edits = snapshot;
            _prepared = prepared;
            return prepared;
        }
    }
}
