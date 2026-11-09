namespace DrillPress;

internal sealed class RuleRegistration(RuleDescriptor descriptor) : CompiledRule(descriptor.Id)
{
    private readonly List<CompiledRule> _clauses = [];

    internal RuleDescriptor Descriptor { get; } = descriptor;

    internal bool IsEmpty => _clauses.Count == 0;

    internal bool RequiresCoverage { get; private set; }

    internal void Add(CompiledRule clause, bool requiresCoverage)
    {
        _clauses.Add(clause);
        RequiresCoverage |= requiresCoverage;
    }

    public override IEnumerable<RuleDiagnostic> Evaluate(
        AnalysisSolution solution,
        ReportingScope reporting
    ) => _clauses.SelectMany(clause => clause.Evaluate(solution, reporting));

    public override void Prepare(AnalysisSolution solution)
    {
        foreach (var clause in _clauses)
            clause.Prepare(solution);
    }

    public override IEnumerable<AnalysisProject> CoverageContexts(
        AnalysisSolution planning,
        ReportingScope reporting
    ) => _clauses.SelectMany(clause => clause.CoverageContexts(planning, reporting));
}
