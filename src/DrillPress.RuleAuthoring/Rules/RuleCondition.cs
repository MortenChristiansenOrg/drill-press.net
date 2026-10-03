namespace DrillPress;

/// <summary>A reusable predicate; composition preserves short-circuit Boolean semantics for selected candidates.</summary>
/// <remarks>Predicates should be pure: known member names may exclude irrelevant candidates before evaluation.</remarks>
public sealed class RuleCondition<T>
{
    private readonly Func<T, bool> _predicate;

    /// <summary>Creates a condition without restricting candidate discovery.</summary>
    public RuleCondition(Func<T, bool> predicate)
        : this(predicate, null) { }

    internal RuleCondition(Func<T, bool> predicate, IReadOnlySet<string>? memberNames)
    {
        _predicate = predicate;
        MemberNames = memberNames;
    }

    internal IReadOnlySet<string>? MemberNames { get; }
    internal bool RequiresCoverage { get; init; }
    internal Func<T, string>? Detail { get; init; }

    /// <summary>Adapts a test-backed coverage requirement to this candidate type while retaining collection metadata.</summary>
    public static implicit operator RuleCondition<T>(CoverageRequirement requirement) =>
        new(requirement.Satisfied<T>) { RequiresCoverage = true, Detail = requirement.Detail<T> };

    /// <summary>Requires both conditions and intersects their known candidate names.</summary>
    public RuleCondition<T> And(RuleCondition<T> other) =>
        new(
            candidate => Evaluate(candidate) && other.Evaluate(candidate),
            MemberNames is null ? other.MemberNames
                : other.MemberNames is null ? MemberNames
                : MemberNames.Intersect(other.MemberNames).ToHashSet()
        )
        {
            RequiresCoverage = RequiresCoverage || other.RequiresCoverage,
            Detail = CombinedDetail(other),
        };

    /// <summary>Accepts either condition; an unrestricted alternative keeps discovery unrestricted.</summary>
    public RuleCondition<T> Or(RuleCondition<T> other) =>
        new(
            candidate => Evaluate(candidate) || other.Evaluate(candidate),
            MemberNames is null || other.MemberNames is null
                ? null
                : MemberNames.Union(other.MemberNames).ToHashSet()
        )
        {
            RequiresCoverage = RequiresCoverage || other.RequiresCoverage,
            Detail = CombinedDetail(other),
        };

    /// <summary>Inverts this condition without restricting candidate discovery.</summary>
    public RuleCondition<T> Not() =>
        new(candidate => !Evaluate(candidate))
        {
            RequiresCoverage = RequiresCoverage,
            Detail = Detail,
        };

    /// <summary>Accepts this condition only when the exception is false.</summary>
    public RuleCondition<T> ExceptWhen(RuleCondition<T> exception) => And(exception.Not());

    internal bool Evaluate(T candidate) => _predicate(candidate);

    private Func<T, string>? CombinedDetail(RuleCondition<T> other)
    {
        if (Detail is not { } left)
            return other.Detail;
        if (other.Detail is not { } right)
            return left;
        return candidate =>
        {
            var first = left(candidate);
            var second = right(candidate);
            return first == second ? first : first + "; " + second;
        };
    }
}
