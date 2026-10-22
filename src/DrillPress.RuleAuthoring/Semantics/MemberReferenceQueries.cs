namespace DrillPress;

/// <summary>Filters for member references.</summary>
public static class MemberReferenceQueries
{
    /// <summary>Selects references to any of the configured fields, properties or method families, binding only names that can match, like <see cref="CodeMember.References"/> for one member.</summary>
    public static CodeQuery<MemberReference> To(
        this CodeQuery<MemberReference> references,
        params CodeMember[] members
    )
    {
        if (members.Length == 0)
            throw new ArgumentException("Select at least one member.", nameof(members));
        var selected = members.ToArray();
        return references.Where(
            new RuleCondition<MemberReference>(
                reference => selected.Any(member => member.Matches(reference.Symbol)),
                NameFilter.Exactly(selected.Select(member => member.Name))
            )
        );
    }
}
