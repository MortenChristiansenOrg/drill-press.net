namespace DrillPress.Semantics;

/// <summary>Semantic conditions that match declarations, including aliases and qualified access.</summary>
public static class Members
{
    /// <summary>Matches the member on a statically referenced declaring type.</summary>
    public static RuleCondition<MemberReference> Are<TDeclaringType>(string memberName) =>
        Are(CodeType.Of<TDeclaringType>(), memberName);

    /// <summary>Matches a named target type, optionally qualified by assembly identity.</summary>
    public static RuleCondition<MemberReference> Are(CodeType declaringType, string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        return new(
            reference =>
                reference.MemberName == memberName
                && (
                    reference.Symbol?.ContainingType is { } symbol
                        ? declaringType.Matches(symbol)
                        : reference.ContainingType.MetadataName == declaringType.MetadataName
                            && (
                                declaringType.AssemblyName is null
                                || reference.ContainingType.AssemblyName
                                    == declaringType.AssemblyName
                            )
                            && (
                                declaringType.TypeArguments.Length == 0
                                || reference.ContainingType.TypeArguments
                                    == declaringType.TypeArguments
                            )
                ),
            new HashSet<string> { memberName }
        );
    }
}
