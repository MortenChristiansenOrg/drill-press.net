namespace DrillPress;

/// <summary>A reusable predicate; composition preserves short-circuit Boolean semantics for selected candidates.</summary>
/// <remarks>Predicates should be pure: known member names may exclude irrelevant candidates before evaluation.</remarks>
public sealed class RuleCondition<T>
{
    private readonly Func<T, bool> _predicate;

    /// <summary>Creates a condition without restricting candidate discovery.</summary>
    public RuleCondition(Func<T, bool> predicate) : this(predicate, null) { }

    internal RuleCondition(Func<T, bool> predicate, IReadOnlySet<string>? memberNames)
    {
        _predicate = predicate;
        MemberNames = memberNames;
    }

    internal IReadOnlySet<string>? MemberNames { get; }

    /// <summary>Requires both conditions and intersects their known candidate names.</summary>
    public RuleCondition<T> And(RuleCondition<T> other) => new(
        candidate => Evaluate(candidate) && other.Evaluate(candidate),
        MemberNames is null ? other.MemberNames : other.MemberNames is null ? MemberNames :
            MemberNames.Intersect(other.MemberNames).ToHashSet());

    /// <summary>Accepts either condition; an unrestricted alternative keeps discovery unrestricted.</summary>
    public RuleCondition<T> Or(RuleCondition<T> other) => new(
        candidate => Evaluate(candidate) || other.Evaluate(candidate),
        MemberNames is null || other.MemberNames is null ? null : MemberNames.Union(other.MemberNames).ToHashSet());

    /// <summary>Inverts this condition without restricting candidate discovery.</summary>
    public RuleCondition<T> Not() => new(candidate => !Evaluate(candidate));

    /// <summary>Accepts this condition only when the exception is false.</summary>
    public RuleCondition<T> ExceptWhen(RuleCondition<T> exception) => And(exception.Not());

    internal bool Evaluate(T candidate) => _predicate(candidate);
}
