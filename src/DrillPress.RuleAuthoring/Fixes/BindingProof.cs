using Microsoft.CodeAnalysis;

namespace DrillPress.Fixes;

/// <summary>Reusable compiler checks for source rewrites. Binding preservation does not establish evaluation order, side effects, lifetime or behavioral equivalence.</summary>
public static class BindingProof
{
    /// <summary>Checks rewritten expression ancestors for unchanged symbols, types and conversions, withholding errors, expression trees and nameof. The caller must separately prove behavior.</summary>
    public static bool PreservesEnclosingExpressions(
        AnalysisSource source,
        SyntaxNode replaced,
        string replacement
    ) => ContextualRewrite.PreservesBinding(source, replaced, replacement);
}
