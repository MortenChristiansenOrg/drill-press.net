namespace DrillPress;

internal sealed class RuleRegistration(RuleDescriptor descriptor) : CompiledRule(descriptor.Id)
{
    private readonly List<CompiledRule> _clauses = [];

    internal RuleDescriptor Descriptor { get; } = descriptor;

    internal void Add(CompiledRule clause) => _clauses.Add(clause);

    public override IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution) =>
        _clauses.SelectMany(clause => clause.Evaluate(solution));

    public override IEnumerable<AnalysisProject> CoverageContexts(AnalysisSolution planning) =>
        _clauses.SelectMany(clause => clause.CoverageContexts(planning));
}
