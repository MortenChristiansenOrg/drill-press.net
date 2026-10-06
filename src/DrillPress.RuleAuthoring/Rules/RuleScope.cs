namespace DrillPress;

/// <summary>Registers readable requirements and prohibitions over a reusable selection.</summary>
public sealed class RuleScope<T>(RuleSet ruleSet, CodeQuery<T> query)
{
    private Func<T, object?>? _reportKey;
    private CodeQuery<AnalysisProject>? _coverageScope;

    internal RuleRegistration? Registration { get; init; }

    /// <summary>Explicitly chooses collection contexts when a custom selector or callback reads coverage facts. The scope must use source-only predicates; built-in coverage conditions otherwise plan candidates automatically.</summary>
    public RuleScope<T> CollectCoverageIn(CodeQuery<AnalysisProject> projects)
    {
        if (projects.RequiresCoverage)
            throw new ArgumentException(
                "Collection scope cannot depend on coverage evidence.",
                nameof(projects)
            );
        return new(ruleSet, query)
        {
            _reportKey = _reportKey,
            _coverageScope = projects,
            Registration = Registration,
        };
    }

    /// <summary>Displays the first source-ordered violation per key and compilation, preferring a violation over review-only items, while retaining every candidate's fix for conflict and combined validation. Null is a valid grouping key.</summary>
    public RuleScope<T> ReportOncePer<TKey>(Func<T, TKey> key) =>
        new(ruleSet, query)
        {
            _reportKey = candidate => key(candidate),
            _coverageScope = _coverageScope,
            Registration = Registration,
        };

    /// <summary>Reports every selected candidate, optionally selecting a precise span, proposing a complete fix, and estimating typical agent fix effort.</summary>
    public RuleScope<T> Forbid(
        string id,
        string message,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null,
        RuleFixComplexity? fixComplexity = null
    )
    {
        return Forbid(
            new RuleDescriptor(id, message) { FixComplexity = fixComplexity },
            location,
            fix
        );
    }

    /// <summary>Reports at an existing source element, syntax node/token or physical location in the candidate's own compilation.</summary>
    public RuleScope<T> Forbid(
        string id,
        string message,
        Func<T, object> at,
        Func<T, FixProposal?>? fix = null,
        RuleFixComplexity? fixComplexity = null
    ) =>
        Forbid(
            id,
            message,
            location: candidate => ReportingLocation.For(candidate, at(candidate)),
            fix: fix,
            fixComplexity: fixComplexity
        );

    /// <summary>Reports failed requirements at an existing contextual source part.</summary>
    public RuleScope<T> Require(
        Func<T, bool> when,
        string id,
        string message,
        Func<T, object> at,
        Func<T, FixProposal?>? fix = null,
        RuleFixComplexity? fixComplexity = null
    ) =>
        Require(
            new RuleCondition<T>(when),
            id,
            message,
            candidate => ReportingLocation.For(candidate, at(candidate)),
            fix,
            fixComplexity
        );

    /// <summary>Registers a reusable descriptor with optional location and fix factories.</summary>
    public RuleScope<T> Forbid(
        RuleDescriptor descriptor,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    )
    {
        ruleSet.Add(
            query,
            descriptor,
            location,
            fix,
            _reportKey,
            coverageScope: _coverageScope,
            registration: Registration
        );
        return this;
    }

    /// <summary>Declares the rule identity before its required predicate.</summary>
    public RuleScope<T> Require(
        string id,
        string message,
        Func<T, bool> when,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null,
        RuleFixComplexity? fixComplexity = null
    ) => Require(new RuleCondition<T>(when), id, message, location, fix, fixComplexity);

    /// <summary>Reports selected candidates that fail the required condition, with optional typical agent fix effort.</summary>
    public RuleScope<T> Require(
        RuleCondition<T> condition,
        string id,
        string message,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null,
        RuleFixComplexity? fixComplexity = null
    ) =>
        Require(
            condition,
            new RuleDescriptor(id, message) { FixComplexity = fixComplexity },
            location,
            fix
        );

    internal RuleScope<T> Require(
        RuleCondition<T> condition,
        RuleDescriptor descriptor,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    )
    {
        ruleSet.Add(
            query.Where(condition.Not()),
            descriptor,
            location,
            fix,
            _reportKey,
            condition.Detail,
            condition.CoverageFacts,
            condition.Failure,
            _coverageScope,
            Registration
        );
        return this;
    }
}
