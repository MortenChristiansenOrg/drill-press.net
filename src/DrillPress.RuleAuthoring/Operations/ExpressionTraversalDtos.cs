namespace DrillPress;

/// <summary>Whether all explicitly configured source-input steps were available within the requested bounds.</summary>
public enum ExpressionTraversalStatus
{
    /// <summary>Every configured step was followed; other expressions are intentional source boundaries.</summary>
    Complete,

    /// <summary>A configured path had invalid binding or an unavailable source input.</summary>
    Unavailable,

    /// <summary>A configured path exceeded a caller-specified traversal bound.</summary>
    LimitExceeded,
}

/// <summary>Why a source-input path could not be completed.</summary>
public enum ExpressionTraversalReason
{
    /// <summary>The expression does not bind without compiler errors.</summary>
    InvalidBinding,

    /// <summary>Dynamic calls, conditional-access envelopes, and explicit or user conversions are not unwrapped.</summary>
    UnsupportedExpression,

    /// <summary>A configured receiver or named input has no explicit source value.</summary>
    UnavailableInput,

    /// <summary>A selected local has no initializer or cannot be proven free of other writes and ref/in/out escapes.</summary>
    LocalNotSingleAssignment,

    /// <summary>The next configured step exceeds the maximum depth.</summary>
    DepthLimit,

    /// <summary>The next distinct expression exceeds the maximum expression count.</summary>
    ExpressionLimit,
}

/// <summary>An incomplete path retains its original source expression and compilation context.</summary>
/// <param name="Expression">Expression at which traversal stopped.</param>
/// <param name="Reason">Typed reason for stopping.</param>
public sealed record ExpressionTraversalBoundary(
    CodeExpression Expression,
    ExpressionTraversalReason Reason
);

/// <summary>Visited expressions in depth-first source order, including the root, with explicit incomplete paths.</summary>
/// <param name="Status">Completion of the configured paths, independently of classification results.</param>
/// <param name="Values">Original source views, including proven local initializers only when explicitly configured.</param>
/// <param name="Boundaries">Paths that could not be followed; empty on complete traversal.</param>
public sealed record ExpressionTraversalResult(
    ExpressionTraversalStatus Status,
    IReadOnlyList<CodeExpression> Values,
    IReadOnlyList<ExpressionTraversalBoundary> Boundaries
);

internal sealed record ExpressionTraversalStep(CodeMember Member, string? Parameter);
