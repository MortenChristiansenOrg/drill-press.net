namespace DrillPress;

/// <summary>Provides semantic conditions for member-reference queries.</summary>
public static class Members
{
    /// <summary>
    /// Matches references to <paramref name="memberName"/> declared by
    /// <typeparamref name="TDeclaringType"/> rather than matching source spelling.
    /// </summary>
    public static RuleCondition<MemberReference> Are<TDeclaringType>(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        var declaringType = CodeType.Of<TDeclaringType>();
        return new RuleCondition<MemberReference>(reference =>
            reference.ContainingType == declaringType &&
            StringComparer.Ordinal.Equals(reference.MemberName, memberName));
    }
}
