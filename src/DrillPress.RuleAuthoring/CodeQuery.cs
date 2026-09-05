namespace DrillPress;

/// <summary>Selects analysis candidates and composes reusable filtering conditions.</summary>
public sealed class CodeQuery<T>
{
    private readonly Func<IReadOnlyList<MemberReference>, IEnumerable<T>> _select;
    private readonly RuleCondition<T>? _condition;

    internal CodeQuery(
        Func<IReadOnlyList<MemberReference>, IEnumerable<T>> select,
        RuleCondition<T>? condition = null)
    {
        _select = select;
        _condition = condition;
    }

    /// <summary>Returns a query restricted to candidates that satisfy <paramref name="condition"/>.</summary>
    public CodeQuery<T> Where(RuleCondition<T> condition) =>
        new(
            _select,
            _condition is null
                ? condition
                : new RuleCondition<T>(candidate =>
                    _condition.Evaluate(candidate) && condition.Evaluate(candidate)));

    internal IEnumerable<T> Evaluate(IReadOnlyList<MemberReference> memberReferences)
    {
        var candidates = _select(memberReferences);
        return _condition is null ? candidates : candidates.Where(_condition.Evaluate);
    }
}
