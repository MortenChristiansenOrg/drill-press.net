using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One expression before and after the complete correction, each bound to its own compilation, for stating why a replacement preserves behavior.</summary>
public sealed class ExpressionChange
{
    internal ExpressionChange(RewriteEvidence rewrite, AnalysisSource rewritten)
    {
        Rewrite = rewrite;
        Before = new(rewrite.Source, (ExpressionSyntax)rewrite.Before);
        After = new(rewritten, (ExpressionSyntax)rewrite.After);
        Kept = Array.AsReadOnly(
            rewrite
                .Inputs.Select(input => new RetainedExpression(
                    new(rewrite.Source, input.Before),
                    Array.AsReadOnly(
                        input.After.Select(after => new CodeExpression(rewritten, after)).ToArray()
                    )
                ))
                .ToArray()
        );
    }

    /// <summary>The replaced expression in the original compilation.</summary>
    public CodeExpression Before { get; }

    /// <summary>The replacement in the fully rewritten compilation.</summary>
    public CodeExpression After { get; }

    /// <summary>Every kept operand with all of its uses in the replacement; zero or several uses are visible here.</summary>
    public IReadOnlyList<RetainedExpression> Kept { get; }

    /// <summary>Low-level syntax, semantic models and the complete edit batch, for proofs needing compiler detail.</summary>
    public RewriteEvidence Rewrite { get; }
}
