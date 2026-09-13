namespace DrillPress;

internal abstract class CompiledRule(string id)
{
    public string Id { get; } = id;

    public abstract IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution);
}
