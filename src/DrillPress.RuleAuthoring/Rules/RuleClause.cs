using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>A typed clause of a rule: the selected candidates, where to report them, and the obligation to check. End the clause with <see cref="Forbid"/> or <c>Require</c>.</summary>
/// <remarks>Clauses are immutable; each modifier returns a new clause. Candidates without a source location must choose one with <c>ReportAt</c>.</remarks>
public sealed class RuleClause<T>
{
    private readonly RuleDefinition _definition;
    private readonly RuleRegistration _registration;
    private readonly CodeQuery<T> _query;
    private readonly Func<T, ReportTarget?>? _target;
    private readonly Func<T, object?>? _reportKey;
    private readonly CodeQuery<AnalysisProject>? _coverageScope;

    internal RuleClause(
        RuleDefinition definition,
        RuleRegistration registration,
        CodeQuery<T> query,
        Func<T, ReportTarget?>? target = null,
        Func<T, object?>? reportKey = null,
        CodeQuery<AnalysisProject>? coverageScope = null
    )
    {
        _definition = definition;
        _registration = registration;
        _query = query;
        _target = target;
        _reportKey = reportKey;
        _coverageScope = coverageScope;
    }

    /// <summary>Reports at another source element in the candidate's own compilation, such as a declaration's type name. Null keeps the candidate's own location.</summary>
    public RuleClause<T> ReportAt(Func<T, ICodeElement?> element) =>
        With(candidate =>
            element(candidate) is { } part ? ReportingLocation.Element(candidate, part) : null
        );

    /// <summary>Reports at original syntax from the candidate's own compilation. Null keeps the candidate's own location.</summary>
    public RuleClause<T> ReportAt(Func<T, SyntaxNode?> syntax) =>
        With(candidate =>
            syntax(candidate) is { } node
                ? ReportingLocation.Span(candidate, node.SyntaxTree, node.Span)
                : null
        );

    /// <summary>Reports at an original token, such as an identifier or modifier, from the candidate's own compilation.</summary>
    public RuleClause<T> ReportAt(Func<T, SyntaxToken> token) =>
        With(candidate =>
            token(candidate) is var part && part.SyntaxTree is { } tree
                ? ReportingLocation.Span(candidate, tree, part.Span)
                : null
        );

    /// <summary>Reports at a physical location in one of the candidate compilation's original documents. Null keeps the candidate's own location.</summary>
    public RuleClause<T> ReportAt(Func<T, SourceLocation?> location) =>
        With(candidate =>
            location(candidate) is { } part ? ReportingLocation.Physical(candidate, part) : null
        );

    /// <summary>Shows one diagnostic per key and compilation: the first in source order, preferring violations over review items. Every grouped candidate's fix is still validated and applied.</summary>
    public RuleClause<T> ReportOncePer<TKey>(Func<T, TKey> key) =>
        new(
            _definition,
            _registration,
            _query,
            _target,
            candidate => key(candidate),
            _coverageScope
        );

    /// <summary>Chooses source-only test collection contexts for custom predicates that read coverage evidence. Built-in coverage requirements plan their candidates automatically.</summary>
    public RuleClause<T> CollectCoverageIn(CodeQuery<AnalysisProject> projects)
    {
        if (projects.RequiresCoverage)
            throw new ArgumentException(
                "Collection scope cannot depend on coverage evidence.",
                nameof(projects)
            );
        return new(_definition, _registration, _query, _target, _reportKey, projects);
    }

    /// <summary>Reports every selected candidate, optionally offering a safe correction.</summary>
    /// <param name="fix">Creates a correction for one candidate; return null when none is safe. Fix chains end with <c>Propose()</c> or <c>SafeWhen(...)</c>.</param>
    /// <returns>The rule, so another clause can be added with <see cref="RuleDefinition.For{T}(CodeQuery{T})"/>.</returns>
    public RuleDefinition Forbid(Func<T, FixProposal?>? fix = null)
    {
        Register(_query, fix, null);
        return _definition;
    }

    /// <summary>Reports selected candidates for which the requirement is false.</summary>
    /// <param name="requirement">A pure predicate that compliant candidates satisfy.</param>
    /// <param name="fix">Creates a correction for one violating candidate; return null when none is safe.</param>
    /// <returns>The rule, so another clause can be added.</returns>
    public RuleDefinition Require(Func<T, bool> requirement, Func<T, FixProposal?>? fix = null) =>
        Require(new RuleCondition<T>(requirement), fix);

    /// <summary>Reports selected candidates failing a reusable condition, including coverage requirements, retaining their evidence, outcome remediation and collection planning.</summary>
    /// <returns>The rule, so another clause can be added.</returns>
    public RuleDefinition Require(RuleCondition<T> requirement, Func<T, FixProposal?>? fix = null)
    {
        Register(_query.Where(requirement.Not()), fix, requirement);
        return _definition;
    }

    private RuleClause<T> With(Func<T, ReportTarget?> target) =>
        new(_definition, _registration, _query, target, _reportKey, _coverageScope);

    private void Register(
        CodeQuery<T> query,
        Func<T, FixProposal?>? fix,
        RuleCondition<T>? requirement
    ) =>
        _registration.Add(
            new CandidateRule<T>(
                query,
                _registration.Descriptor,
                _target,
                fix,
                _reportKey,
                requirement?.Detail,
                requirement?.CoverageFacts,
                requirement?.Failure,
                _coverageScope
            ),
            query.RequiresCoverage || _coverageScope is not null
        );
}
