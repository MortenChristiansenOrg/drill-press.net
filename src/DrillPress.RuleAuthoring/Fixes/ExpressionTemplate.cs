using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A parsed expression with annotated original inputs; unused and repeated holes retain their full correspondence for proofs.</summary>
internal sealed class ExpressionTemplate
{
    internal ExpressionTemplate(ExpressionSyntax syntax, IReadOnlyList<ExpressionInput> inputs)
    {
        Syntax = syntax;
        Inputs = inputs;
    }

    /// <summary>Precedence-safe replacement syntax; semantic binding is checked in the rewritten compilation.</summary>
    internal ExpressionSyntax Syntax { get; }

    /// <summary>Every supplied original input, including unused ones.</summary>
    internal IReadOnlyList<ExpressionInput> Inputs { get; }
}
