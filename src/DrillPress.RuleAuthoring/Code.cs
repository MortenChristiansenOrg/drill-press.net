namespace DrillPress;

/// <summary>Provides the root queries available to compiled rule definitions.</summary>
public static class Code
{
    /// <summary>Selects source expressions that semantic analysis bound to a member.</summary>
    public static CodeQuery<MemberReference> MemberReferences { get; } =
        new(static memberReferences => memberReferences);
}
