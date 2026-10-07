using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>The result of a bounded proof. Unknown is not approval.</summary>
public enum ProofResult
{
    /// <summary>The available evidence does not establish the requested invariant.</summary>
    Unknown,

    /// <summary>The evidence establishes the invariant under the proof's documented assumptions.</summary>
    Proven,

    /// <summary>The evidence contradicts the requested invariant.</summary>
    Disproven,
}

/// <summary>One original node mapped into the fully rewritten compilation.</summary>
/// <param name="Source">The original compilation membership.</param>
/// <param name="Before">The original node.</param>
/// <param name="After">The mapped rewritten node; its syntax kind may have changed.</param>
/// <param name="Model">The semantic model of the fully rewritten tree.</param>
public sealed record NodeRewrite(
    AnalysisSource Source,
    SyntaxNode Before,
    SyntaxNode After,
    SemanticModel Model
);

/// <summary>Explicit occurrence correspondence for one retained input in a replacement.</summary>
/// <param name="Before">The original operand within the replaced expression.</param>
/// <param name="After">Every annotated use in the replacement; empty or multiple entries can disprove count preservation.</param>
public sealed record InputRewrite(ExpressionSyntax Before, IReadOnlyList<ExpressionSyntax> After);

/// <summary>A retained capture with every corresponding rewritten occurrence.</summary>
/// <param name="Before">The original capture.</param>
/// <param name="After">All mapped uses; zero or multiple uses do not imply count preservation.</param>
public sealed record RetainedExpression(CodeExpression Before, IReadOnlyList<CodeExpression> After);
