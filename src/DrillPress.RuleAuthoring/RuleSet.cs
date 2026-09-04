namespace DrillPress;

/// <summary>Collects compiled rule declarations and evaluates them against discovered candidates.</summary>
public sealed class RuleSet
{
    private readonly List<ICompiledRule> _rules = [];

    /// <summary>Begins a rule declaration over candidates selected by <paramref name="query"/>.</summary>
    public RuleScope<T> For<T>(CodeQuery<T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new RuleScope<T>(this, query);
    }

    /// <summary>Evaluates every registered rule and returns diagnostics in deterministic order.</summary>
    public IReadOnlyList<RuleDiagnostic> Evaluate(IReadOnlyList<MemberReference> memberReferences) =>
        _rules
            .SelectMany(rule => rule.Evaluate(memberReferences))
            .OrderBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Start)
            .ToArray();

    internal void Add<T>(CodeQuery<T> query, string id, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (_rules.Any(rule => StringComparer.Ordinal.Equals(rule.Id, id)))
        {
            throw new InvalidOperationException($"Rule id '{id}' is registered more than once.");
        }

        _rules.Add(new CompiledRule<T>(query, new RuleDescriptor(id, message)));
    }

    private interface ICompiledRule
    {
        string Id { get; }

        IEnumerable<RuleDiagnostic> Evaluate(IReadOnlyList<MemberReference> memberReferences);
    }

    private sealed class CompiledRule<T>(CodeQuery<T> query, RuleDescriptor descriptor) : ICompiledRule
    {
        public string Id => descriptor.Id;

        public IEnumerable<RuleDiagnostic> Evaluate(IReadOnlyList<MemberReference> memberReferences) =>
            query.Evaluate(memberReferences)
                .Select(candidate => candidate is MemberReference reference
                    ? new RuleDiagnostic(descriptor, reference.Location)
                    : throw new InvalidOperationException(
                        $"Candidate type '{typeof(T)}' does not expose a source location."));
    }
}
