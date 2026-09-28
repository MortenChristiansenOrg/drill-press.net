using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>Structural correlation under the configured root relation; never a runtime object-alias proof.</summary>
public enum PathCorrelation
{
    /// <summary>Evidence is incomplete or unsupported.</summary>
    Unknown,

    /// <summary>The root relation, bound members and typed constant indices agree.</summary>
    Match,

    /// <summary>At least one established structural component differs.</summary>
    Different,
}

/// <summary>A typed compile-time path index, retaining null separately from missing evidence.</summary>
/// <param name="Type">The compiler type after conversion to the array/indexer argument.</param>
/// <param name="Value">The constant value.</param>
public sealed record PathConstant(ITypeSymbol Type, object? Value);

/// <summary>A resolved member or array/indexer access in a path.</summary>
/// <param name="Member">The substituted property/field/indexer symbol; null denotes an array access.</param>
/// <param name="Indices">Constant indices in parameter order; empty for ordinary member access.</param>
/// <param name="Type">The static type produced by this step.</param>
public sealed record MemberPathStep(
    ISymbol? Member,
    IReadOnlyList<PathConstant> Indices,
    ITypeSymbol Type
);

/// <summary>One parsed name or indexing step. Exactly one of Name and nonempty Indices must be supplied.</summary>
/// <param name="Name">The configured external member name.</param>
/// <param name="Indices">Typed constant indices; parsing does not bind them to any model.</param>
public sealed record MemberKeySegment(string? Name, IReadOnlyList<MemberKeyIndex> Indices);

/// <summary>A parser-supplied primitive constant index.</summary>
/// <param name="Type">Its explicit primitive type.</param>
/// <param name="Value">The constant value; no runtime expression is evaluated.</param>
public sealed record MemberKeyIndex(SpecialType Type, object? Value);
