namespace DrillPress;

/// <summary>Context-local evidence for one added argument: both calls, the new value bound in the rewritten compilation and any verified overload change.</summary>
public sealed class ArgumentAdditionChange
{
    internal ArgumentAdditionChange(
        RewriteEvidence rewrite,
        CodeInvocation before,
        CodeInvocation after,
        CodeExpression addedValue,
        MethodPair? expected
    )
    {
        Rewrite = rewrite;
        Before = before;
        After = after;
        AddedValue = addedValue;
        Expected = expected;
    }

    /// <summary>The complete batch mapping and semantic models.</summary>
    public RewriteEvidence Rewrite { get; }

    /// <summary>The original call, including the compiler-supplied value the new argument replaces when the overload stays the same.</summary>
    public CodeInvocation Before { get; }

    /// <summary>The rebound call in the rewritten compilation.</summary>
    public CodeInvocation After { get; }

    /// <summary>The added expression, bound in the rewritten compilation.</summary>
    public CodeExpression AddedValue { get; }

    /// <summary>The configured overload pair as resolved in this original context, or null when the call keeps its overload.</summary>
    public MethodPair? Expected { get; }

    /// <summary>Tells whether a symbol read before the change and one read after it denote the same entity.</summary>
    public bool Same(
        Microsoft.CodeAnalysis.ISymbol? before,
        Microsoft.CodeAnalysis.ISymbol? after
    ) => Rewrite.Context.Same(before, after);
}
