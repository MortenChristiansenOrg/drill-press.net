namespace DrillPress;

/// <summary>Source-context filters for references that a rewrite cannot change, such as those inside nameof.</summary>
public static class ExpressionContextQueries
{
    /// <summary>Selects member references outside compiler-bound nameof operands. Member reference queries otherwise include nameof uses.</summary>
    public static CodeQuery<MemberReference> OutsideNameOf(
        this CodeQuery<MemberReference> references
    ) => references.Where(reference => !reference.Facts.IsInsideNameOf);

    /// <summary>Selects type references outside compiler-bound nameof operands.</summary>
    public static CodeQuery<CodeTypeReference> OutsideNameOf(
        this CodeQuery<CodeTypeReference> references
    ) => references.Where(reference => !reference.Facts.IsInsideNameOf);
}
