using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Evidence for one transformation after the complete atomic batch has been applied in one context.</summary>
public sealed class RewriteEvidence
{
    internal RewriteEvidence(
        RewriteContext context,
        NodeRewrite target,
        IReadOnlyList<InputRewrite> inputs
    )
    {
        Context = context;
        Target = target;
        Inputs = inputs;
    }

    /// <summary>Semantic expression views bound to their actual original and rewritten sources; absent for declaration or statement edits.</summary>
    public ExpressionRewrite? Expressions =>
        Before is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax before
        && After is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax after
        && Context.RewrittenSource(Source) is { } rewritten
            ? new(new(Source, before), new(rewritten, after))
            : null;

    /// <summary>Original and rewritten semantic views of each explicitly mapped input, including missing or repeated uses.</summary>
    public IReadOnlyList<RetainedExpression> RetainedExpressions =>
        Context.RewrittenSource(Source) is { } rewritten
            ? Inputs
                .Select(input => new RetainedExpression(
                    new(Source, input.Before),
                    Array.AsReadOnly(
                        input.After.Select(after => new CodeExpression(rewritten, after)).ToArray()
                    )
                ))
                .ToArray()
            : [];

    /// <summary>The complete rewritten compilation and original-text edits.</summary>
    public RewriteContext Context { get; }

    /// <summary>The selected before/after node and rewritten semantic model.</summary>
    public NodeRewrite Target { get; }

    /// <summary>The actual original membership being validated, including linked contexts without findings.</summary>
    public AnalysisSource Source => Target.Source;

    /// <summary>The selected original node in this compilation.</summary>
    public SyntaxNode Before => Target.Before;

    /// <summary>The mapped replacement in this compilation.</summary>
    public SyntaxNode After => Target.After;

    /// <summary>The original semantic model.</summary>
    public SemanticModel BeforeModel => Source.Model;

    /// <summary>The semantic model after all proposed edits.</summary>
    public SemanticModel AfterModel => Target.Model;

    /// <summary>Explicit retained-operand mappings, excluding unregistered syntax.</summary>
    public IReadOnlyList<InputRewrite> Inputs { get; }
}
