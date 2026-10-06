namespace DrillPress;

/// <summary>Collects compiled rule declarations and evaluates them against discovered candidates.</summary>
public sealed class RuleSet
{
    private readonly List<CompiledRule> _rules = [];

    internal bool RequiresCoverage { get; private set; }

    internal IReadOnlySet<AnalysisProject> CoverageContexts(AnalysisSolution solution)
    {
        var planning = new AnalysisSolution(
            solution.Projects,
            solution.Options,
            solution.CancellationToken
        )
        {
            IsPlanningCoverage = true,
        };
        return _rules
            .SelectMany(rule => rule.CoverageContexts(planning))
            .Where(project => project.Snapshot.IsAnalysisTarget)
            .ToHashSet();
    }

    /// <summary>Begins a rule declaration over candidates selected by <paramref name="query"/>.</summary>
    public RuleScope<T> For<T>(CodeQuery<T> query) => new(this, query);

    /// <summary>Reserves a unique rule identity for explicitly composed typed clauses, sharing remediation and optional typical fix effort.</summary>
    public RuleDefinition Rule(
        string id,
        string message,
        RuleFixComplexity? fixComplexity = null
    ) => Rule(new RuleDescriptor(id, message) { FixComplexity = fixComplexity });

    /// <summary>Reserves the descriptor's identity for typed clauses. Other declarations, including another definition with the same descriptor, cannot reuse its ID.</summary>
    public RuleDefinition Rule(RuleDescriptor descriptor) => new(this, Register(descriptor));

    /// <summary>Evaluates every registered rule and returns diagnostics in deterministic order.</summary>
    public IReadOnlyList<RuleDiagnostic> Evaluate(
        IReadOnlyList<MemberReference> memberReferences
    ) => Evaluate(new AnalysisSolution(memberReferences));

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

    internal void Add<T>(
        CodeQuery<T> query,
        RuleDescriptor descriptor,
        Func<T, SourceLocation>? location,
        Func<T, FixProposal?>? fix,
        Func<T, object?>? reportKey = null,
        Func<T, string>? detail = null,
        Func<T, IReadOnlyList<CoverageEvidence>>? coverageFacts = null,
        Func<T, ConditionFailure>? failure = null,
        CodeQuery<AnalysisProject>? coverageScope = null,
        RuleRegistration? registration = null
    )
    {
        if (registration is null)
            ValidateRegistration(descriptor);
        RequiresCoverage |= query.RequiresCoverage || coverageScope is not null;
        var clause = new CandidateRule<T>(
            query,
            descriptor,
            location,
            fix,
            reportKey,
            detail,
            coverageFacts,
            failure,
            coverageScope
        );
        if (registration is null)
            _rules.Add(clause);
        else
            registration.Add(clause);
    }

    private RuleRegistration Register(RuleDescriptor descriptor)
    {
        ValidateRegistration(descriptor);
        var registration = new RuleRegistration(descriptor);
        _rules.Add(registration);
        return registration;
    }

    private void ValidateRegistration(RuleDescriptor descriptor)
    {
        ValidateDescriptor(descriptor);
        if (_rules.Any(rule => rule.Id == descriptor.Id))
            throw new InvalidOperationException(
                $"Rule id '{descriptor.Id}' is registered more than once."
            );
    }

    private static void ValidateDescriptor(RuleDescriptor descriptor)
    {
        var (id, message) = descriptor;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (descriptor.FixComplexity is { } complexity && !Enum.IsDefined(complexity))
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "Unknown rule fix complexity."
            );
        if (
            id.Any(char.IsControl)
            || message.Any(char.IsControl)
            || id.Contains('\u2028')
            || id.Contains('\u2029')
            || message.Contains('\u2028')
            || message.Contains('\u2029')
        )
        {
            throw new ArgumentException(
                "Rule identifiers and remediation messages must be single-line."
            );
        }
    }
}
