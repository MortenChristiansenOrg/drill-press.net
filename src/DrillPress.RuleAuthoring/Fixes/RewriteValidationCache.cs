using DrillPress.Manifest;
using Microsoft.CodeAnalysis;

namespace DrillPress;

internal sealed class RewriteValidationCache(AnalysisProject project)
{
    private readonly object _gate = new();
    private readonly Lazy<bool> _originalHasErrors = new(() =>
        project
            .Compilation.GetDiagnostics(project.CancellationToken)
            .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
    );
    private SourceEdit[]? _edits;
    private PreparedRewrite _prepared;

    internal bool OriginalHasErrors => _originalHasErrors.Value;

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
