using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A selected call ready for replacement or argument removal.</summary>
public sealed class InvocationFix : ExpressionFix
{
    internal InvocationFix(AnalysisSource source, ExpressionSyntax call)
        : base(source, call) { }

    /// <summary>Removes the explicit argument bound to a declaration parameter, under an expected overload change and separate value and evaluation proofs.</summary>
    public ArgumentRemoval RemoveArgument(string parameter) => new(Source, Target, parameter);
}
