namespace DrillPress;

internal sealed class CandidateRule<T>(
    CodeQuery<T> query,
    RuleDescriptor descriptor,
    Func<T, SourceLocation>? location,
    Func<T, FixProposal?>? fix
) : CompiledRule(descriptor.Id)
{
    public override IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution) =>
        query
            .Evaluate(solution)
            .Where(candidate => candidate is not ICodeElement { Source.Document.IsGenerated: true })
            .Select(candidate =>
            {
                solution.CancellationToken.ThrowIfCancellationRequested();
                var element = candidate as ICodeElement;
                var span = location is not null
                    ? location(candidate)
                    : element?.Location
                        ?? throw new InvalidOperationException(
                            $"Candidate type '{typeof(T)}' does not expose a source location."
                        );
                return new RuleDiagnostic(descriptor, span)
                {
                    Source = element?.Source,
                    Fix = fix?.Invoke(candidate),
                };
            });
}
