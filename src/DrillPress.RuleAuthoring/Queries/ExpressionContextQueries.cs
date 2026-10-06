namespace DrillPress;

/// <summary>Explicit source-context filters that exclude candidates without original expression evidence.</summary>
public static class ExpressionContextQueries
{
    /// <summary>Selects source-bound references outside compiler-bound nameof operands. Ordinary member-reference queries retain nameof references.</summary>
    public static CodeQuery<MemberReference> OutsideNameOf(
        this CodeQuery<MemberReference> references
    ) => references.Where(reference => reference.Facts is { IsInsideNameOf: false });
}
