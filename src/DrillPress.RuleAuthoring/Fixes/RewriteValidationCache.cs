using DrillPress.Manifest;
using Microsoft.CodeAnalysis;

namespace DrillPress.Fixes;

internal sealed class RewriteValidationCache(AnalysisProject project)
{
    private readonly object _gate = new();
    private readonly Lazy<bool> _originalHasErrors = new(() =>
        project
            .Compilation.GetDiagnostics(project.CancellationToken)
            .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
    );
    private SourceEdit[]? _edits;
    private RewriteContext? _context;

    internal bool OriginalHasErrors => _originalHasErrors.Value;

    internal RewriteContext? Get(
        IReadOnlyList<SourceEdit> edits,
        Func<SourceEdit[], RewriteContext?> create
    )
    {
        project.CancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_edits is not null && _edits.SequenceEqual(edits))
                return _context;
            var snapshot = edits.ToArray();
            var context = create(snapshot);
            // Bound retention to one compilation, and compare edit values rather than mutable list identity.
            _edits = snapshot;
            _context = context;
            return context;
        }
    }
}
