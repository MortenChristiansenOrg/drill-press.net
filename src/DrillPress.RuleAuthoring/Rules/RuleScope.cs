namespace DrillPress;

/// <summary>Registers readable requirements and prohibitions over a reusable selection.</summary>
public sealed class RuleScope<T>(RuleSet ruleSet, CodeQuery<T> query)
{
    private Func<T, object?>? _reportKey;

    /// <summary>Displays the first source-ordered violation per key and compilation while retaining every candidate's fix for conflict and combined validation. Null is a valid grouping key.</summary>
    public RuleScope<T> ReportOncePer<TKey>(Func<T, TKey> key) =>
        new(ruleSet, query) { _reportKey = candidate => key(candidate) };

    /// <summary>Reports every selected candidate, optionally selecting a precise span and proposing a complete fix.</summary>
    public RuleScope<T> Forbid(
        string id,
        string message,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    )
    {
        return Forbid(new RuleDescriptor(id, message), location, fix);
    }

    /// <summary>Reports at an existing source element, syntax node/token or physical location in the candidate's own compilation.</summary>
    public RuleScope<T> Forbid(
        string id,
        string message,
        Func<T, object> at,
        Func<T, FixProposal?>? fix = null
    ) =>
        Forbid(
            id,
            message,
            location: candidate => ReportingLocation.For(candidate, at(candidate)),
            fix: fix
        );

    /// <summary>Reports failed requirements at an existing contextual source part.</summary>
    public RuleScope<T> Require(
        Func<T, bool> when,
        string id,
        string message,
        Func<T, object> at,
        Func<T, FixProposal?>? fix = null
    ) =>
        Require(
            new RuleCondition<T>(when),
            id,
            message,
            candidate => ReportingLocation.For(candidate, at(candidate)),
            fix
        );

    /// <summary>Registers a reusable descriptor with optional location and fix factories.</summary>
    public RuleScope<T> Forbid(
        RuleDescriptor descriptor,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    )
    {
        ruleSet.Add(query, descriptor, location, fix, _reportKey);
        return this;
    }

    /// <summary>Declares the rule identity before its required predicate.</summary>
    public RuleScope<T> Require(
        string id,
        string message,
        Func<T, bool> when,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    ) => Require(new RuleCondition<T>(when), id, message, location, fix);

    /// <summary>Reports selected candidates that fail the required condition.</summary>
    public RuleScope<T> Require(
        RuleCondition<T> condition,
        string id,
        string message,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    )
    {
        ruleSet.Add(
            query.Where(condition.Not()),
            new RuleDescriptor(id, message),
            location,
            fix,
            _reportKey,
            condition.Detail
        );
        return this;
    }
}
