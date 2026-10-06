namespace DrillPress;

/// <summary>Immutable consumer configuration for read-only traversal through selected call inputs; matches imply no runtime identity, purity, or rewrite permission.</summary>
public sealed class ExpressionTraversal
{
    private readonly ExpressionTraversalStep[] _steps;
    private readonly bool _throughLocals;

    /// <summary>Starts with no forwarding calls; unconfigured expressions are traversal boundaries.</summary>
    public ExpressionTraversal()
        : this([]) { }

    private ExpressionTraversal(ExpressionTraversalStep[] steps, bool throughLocals = false)
    {
        _steps = steps;
        _throughLocals = throughLocals;
    }

    /// <summary>Opt-in traversal through local declarator initializers proven to be the local's only write. Ref/in/out escapes and nested-function writes remain typed boundaries.</summary>
    public ExpressionTraversal ThroughSingleAssignmentLocals() => new(_steps, true);

    /// <summary>Follows explicit instance or extension receivers of these configured identities, including static extension spelling.</summary>
    public ExpressionTraversal ThroughReceiverOf(params CodeMember[] members) =>
        new(
            [.. _steps, .. members.Select(member => new ExpressionTraversalStep(member, null))],
            _throughLocals
        );

    /// <summary>Follows source values bound to the named declaration parameter, independently of named-argument order. Omitted/default inputs are unavailable.</summary>
    public ExpressionTraversal ThroughArgumentOf(CodeMember member, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return new([.. _steps, new(member, parameterName)], _throughLocals);
    }

    internal ExpressionTraversalResult Traverse(
        CodeExpression root,
        int maxDepth,
        int maxExpressions
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExpressions, 1);
        return new ExpressionTraversalWalker(
            _steps,
            _throughLocals,
            maxDepth,
            maxExpressions
        ).Traverse(root);
    }
}
