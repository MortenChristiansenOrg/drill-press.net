using DrillPress.Manifest;
using Microsoft.CodeAnalysis;

namespace DrillPress.Engine;

internal sealed class SnapshotTreeOptions(Dictionary<SyntaxTree, DocumentSnapshot> documents) : SyntaxTreeOptionsProvider
{
    private readonly Dictionary<SyntaxTree, DocumentSnapshot> _documents = documents;

    public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) =>
        _documents[tree].IsGenerated ? GeneratedKind.MarkedGenerated : GeneratedKind.NotGenerated;

    public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        if (_documents[tree].Options is { } options && options.DiagnosticOptions.TryGetValue(diagnosticId, out var value))
        {
            severity = (ReportDiagnostic)value;
            return true;
        }

        severity = ReportDiagnostic.Default;
        return false;
    }

    public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        severity = ReportDiagnostic.Default;
        return false;
    }
}
