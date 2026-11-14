namespace DrillPress;

/// <summary>Whether the declarations that supply a value describe its nullability. A value from a nullable-oblivious declaration, such as a library compiled without nullable reference types, gets a not-null flow state that is no evidence it can't be null.</summary>
public enum NullableOrigin
{
    /// <summary>Every source was found, and each is either a language fact such as a literal or construction, or a declaration in a nullable-aware context.</summary>
    Aware,

    /// <summary>At least one source is declared in a nullable-oblivious context or metadata without annotations.</summary>
    Oblivious,

    /// <summary>No oblivious source was found, but analysis could not follow every source; see the boundaries.</summary>
    Unknown,
}

/// <summary>One expression that supplies a value, after following locals and parameters to the assignments that reach them.</summary>
/// <param name="Expression">The supplying expression, such as a call, member read or literal, or a parameter read for its value on entry.</param>
/// <param name="Origin">Aware or Oblivious.</param>
public sealed record NullableOriginValue(CodeExpression Expression, NullableOrigin Origin);

/// <summary>Where a value's nullability evidence comes from. Nothing is executed, and an aware origin is still only an annotation.</summary>
/// <param name="Origin">Oblivious when any value is oblivious, otherwise Unknown when analysis stopped or found nothing, otherwise Aware.</param>
/// <param name="Values">The supplying expressions in traversal order.</param>
/// <param name="Boundaries">Where analysis stopped: untracked assignments, unsupported expressions, invalid binding or exhausted bounds.</param>
public sealed record NullableOriginAnalysis(
    NullableOrigin Origin,
    IReadOnlyList<NullableOriginValue> Values,
    IReadOnlyList<ExpressionTraversalBoundary> Boundaries
);
