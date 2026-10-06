namespace DrillPress;

/// <summary>Registers typed obligations under a shared rule definition without losing condition metadata.</summary>
public sealed class RuleClause<T>
{
    private readonly RuleScope<T> _scope;
    private readonly RuleDescriptor _descriptor;

    internal RuleClause(RuleScope<T> scope, RuleDescriptor descriptor) =>
        (_scope, _descriptor) = (scope, descriptor);

    /// <summary>Chooses source-only collection contexts for a clause whose custom callbacks read coverage. Built-in requirements plan their selected candidates automatically.</summary>
    public RuleClause<T> CollectCoverageIn(CodeQuery<AnalysisProject> projects) =>
        new(_scope.CollectCoverageIn(projects), _descriptor);

    /// <summary>Groups this clause's findings per key and compilation, preferring violations to reviews and retaining every fix. Keys do not group other clauses.</summary>
    public RuleClause<T> ReportOncePer<TKey>(Func<T, TKey> key) =>
        new(_scope.ReportOncePer(key), _descriptor);

    /// <summary>Reports each selected candidate under the shared descriptor, optionally choosing a physical location and complete fix proposal.</summary>
    public RuleClause<T> Forbid(
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    )
    {
        _scope.Forbid(_descriptor, location, fix);
        return this;
    }

    /// <summary>Reports at an existing contextual source part in the candidate's own compilation.</summary>
    public RuleClause<T> Forbid(Func<T, object> at, Func<T, FixProposal?>? fix = null) =>
        Forbid(location: candidate => ReportingLocation.For(candidate, at(candidate)), fix: fix);

    /// <summary>Reports failed requirements while preserving typed evidence, outcome remediation, gating and collection planning.</summary>
    public RuleClause<T> Require(
        RuleCondition<T> condition,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    )
    {
        _scope.Require(condition, _descriptor, location, fix);
        return this;
    }

    /// <summary>Reports selected candidates that fail a pure consumer predicate under the shared descriptor.</summary>
    public RuleClause<T> Require(
        Func<T, bool> when,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    ) => Require(new RuleCondition<T>(when), location, fix);

    /// <summary>Reports failed requirements at an existing contextual source part, retaining the requirement's metadata.</summary>
    public RuleClause<T> Require(
        RuleCondition<T> condition,
        Func<T, object> at,
        Func<T, FixProposal?>? fix = null
    ) => Require(condition, candidate => ReportingLocation.For(candidate, at(candidate)), fix);

    /// <summary>Reports failed consumer predicates at an existing contextual source part.</summary>
    public RuleClause<T> Require(
        Func<T, bool> when,
        Func<T, object> at,
        Func<T, FixProposal?>? fix = null
    ) => Require(new RuleCondition<T>(when), at, fix);
}
