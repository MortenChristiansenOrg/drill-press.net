namespace DrillPress;

/// <summary>Collects compiled rule declarations and evaluates them against discovered candidates.</summary>
public sealed class RuleSet
{
    private readonly List<CompiledRule> _rules = [];

    /// <summary>Begins a rule declaration over candidates selected by <paramref name="query"/>.</summary>
    public RuleScope<T> For<T>(CodeQuery<T> query) => new(this, query);

    /// <summary>Evaluates every registered rule and returns diagnostics in deterministic order.</summary>
    public IReadOnlyList<RuleDiagnostic> Evaluate(IReadOnlyList<MemberReference> memberReferences) => Evaluate(new AnalysisSolution(memberReferences));

    /// <summary>Evaluates all registered rules over one shared source graph.</summary>
    public IReadOnlyList<RuleDiagnostic> Evaluate(AnalysisSolution solution)
    {
        solution.CancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<RuleDiagnostic>();
        foreach (var rule in _rules)
        {
            using var measurement = solution.Options.Profile.Measure("rule." + rule.Id);
            diagnostics.AddRange(rule.Evaluate(solution));
        }

        solution.WriteProfileCounters();
        return diagnostics
            .OrderBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Start)
            .ToArray();
    }

    internal void Add<T>(CodeQuery<T> query, RuleDescriptor descriptor, Func<T, SourceLocation>? location, Func<T, FixProposal?>? fix)
    {
        var (id, message) = descriptor;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (id.Any(char.IsControl) || message.Any(char.IsControl) || id.Contains('\u2028') || id.Contains('\u2029') ||
            message.Contains('\u2028') || message.Contains('\u2029'))
        {
            throw new ArgumentException("Rule identifiers and remediation messages must be single-line.");
        }

        if (_rules.Any(rule => rule.Id == id))
        {
            throw new InvalidOperationException($"Rule id '{id}' is registered more than once.");
        }

        _rules.Add(new CandidateRule<T>(query, descriptor, location, fix));
    }

}
