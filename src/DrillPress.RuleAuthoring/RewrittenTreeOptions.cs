using Microsoft.CodeAnalysis;

namespace DrillPress;

internal sealed class RewrittenTreeOptions(SyntaxTreeOptionsProvider original, SyntaxTree before, SyntaxTree after) : SyntaxTreeOptionsProvider
{
    public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) =>
        original.IsGenerated(tree == after ? before : tree, cancellationToken);

    public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken,
        out ReportDiagnostic severity) => original.TryGetDiagnosticValue(tree == after ? before : tree, diagnosticId, cancellationToken, out severity);

    public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity) =>
        original.TryGetGlobalDiagnosticValue(diagnosticId, cancellationToken, out severity);
}
