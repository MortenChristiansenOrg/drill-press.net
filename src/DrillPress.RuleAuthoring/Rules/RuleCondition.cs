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
    internal Func<T, IReadOnlyList<CoverageEvidence>>? CoverageFacts { get; init; }
    internal Func<T, ConditionFailure>? Failure { get; init; }
    internal Func<T, ConditionPossibilities>? BeforeCoverage { get; init; }

    /// <summary>Adapts a test-backed coverage requirement to this candidate type while retaining collection metadata.</summary>
    public static implicit operator RuleCondition<T>(CoverageRequirement requirement) =>
        new(requirement.Satisfied<T>)
        {
            RequiresCoverage = true,
            Detail = requirement.Detail<T>,
            CoverageFacts = candidate => new[] { requirement.InspectCandidate(candidate) },
            Failure = requirement.Failure<T>,
            BeforeCoverage = _ => new(true, true),
        };

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
            CoverageFacts = CombinedCoverage(other),
            Failure = CombinedFailure(other),
            BeforeCoverage = candidate =>
            {
                var left = Possibilities(candidate);
                if (!left.CanBeTrue)
                    return new(false, true);
                var right = other.Possibilities(candidate);
                return new(left.CanBeTrue && right.CanBeTrue, left.CanBeFalse || right.CanBeFalse);
            },
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
            CoverageFacts = CombinedCoverage(other),
            Failure = CombinedFailure(other),
            BeforeCoverage = candidate =>
            {
                var left = Possibilities(candidate);
                if (!left.CanBeFalse)
                    return new(true, false);
                var right = other.Possibilities(candidate);
                return new(left.CanBeTrue || right.CanBeTrue, left.CanBeFalse && right.CanBeFalse);
            },
        };

    /// <summary>Inverts this condition without restricting candidate discovery.</summary>
    public RuleCondition<T> Not() =>
        new(candidate => !Evaluate(candidate))
        {
            RequiresCoverage = RequiresCoverage,
            Detail = Detail,
            CoverageFacts = CoverageFacts,
            BeforeCoverage = candidate =>
            {
                var inner = Possibilities(candidate);
                return new(inner.CanBeFalse, inner.CanBeTrue);
            },
        };

    /// <summary>Accepts this condition only when the exception is false.</summary>
    public RuleCondition<T> ExceptWhen(RuleCondition<T> exception) => And(exception.Not());

    internal bool Evaluate(T candidate) => _predicate(candidate);

    private Func<T, ConditionFailure>? CombinedFailure(RuleCondition<T> other)
    {
        if (Failure is null && other.Failure is null)
            return null;
        return candidate =>
        {
            var failures = new[] { this, other }
                .Where(condition => !condition.Evaluate(candidate))
                .Select(condition => condition.Failure?.Invoke(candidate) ?? new())
                .ToArray();
            var messages = failures
                .Select(failure => failure.Remediation)
                .OfType<string>()
                .Distinct()
                .ToArray();
            return new(
                failures.Length > 0
                && failures.All(failure => failure.Disposition == FindingDisposition.Review)
                    ? FindingDisposition.Review
                    : FindingDisposition.Violation,
                messages.Length == 0 ? null : string.Join("; ", messages)
            );
        };
    }

    internal ConditionPossibilities Possibilities(T candidate)
    {
        if (BeforeCoverage is { } planning)
            return planning(candidate);
        var value = Evaluate(candidate);
        return new(value, !value);
    }

    private Func<T, IReadOnlyList<CoverageEvidence>>? CombinedCoverage(RuleCondition<T> other)
    {
        if (CoverageFacts is not { } left)
            return other.CoverageFacts;
        if (other.CoverageFacts is not { } right)
            return left;
        return candidate => left(candidate).Concat(right(candidate)).ToArray();
    }

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
