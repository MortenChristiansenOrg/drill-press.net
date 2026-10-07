using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal static class ExpressionLayout
{
    // Adds parentheses only when the destination would otherwise parse the replacement differently.
    internal static ExpressionSyntax Fit(
        AnalysisSource source,
        ExpressionSyntax original,
        ExpressionSyntax replacement
    )
    {
        var normalized = replacement.NormalizeWhitespace();
        if (
            normalized is ParenthesizedExpressionSyntax
            || ParsesInPlace(source, original, normalized)
        )
            return normalized;
        return SyntaxFactory.ParenthesizedExpression(normalized);
    }

    private static bool ParsesInPlace(
        AnalysisSource source,
        ExpressionSyntax original,
        ExpressionSyntax replacement
    )
    {
        var text = replacement.ToString();
        var tree = source.Tree.WithChangedText(
            source
                .Tree.GetText(source.Project.CancellationToken)
                .WithChanges(new TextChange(original.Span, text))
        );
        var span = new TextSpan(original.SpanStart, text.Length);
        var node = tree.GetRoot(source.Project.CancellationToken)
            .FindNode(span, getInnermostNodeForTie: true);
        return node is ExpressionSyntax parsed
            && parsed.Span == span
            && !parsed.ContainsDiagnostics
            && parsed.Parent?.RawKind == original.Parent?.RawKind
            && SyntaxFactory.AreEquivalent(parsed, replacement);
    }
}
