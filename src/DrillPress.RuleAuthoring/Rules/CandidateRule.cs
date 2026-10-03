namespace DrillPress;

internal sealed class CandidateRule<T>(
    CodeQuery<T> query,
    RuleDescriptor descriptor,
    Func<T, SourceLocation>? location,
    Func<T, FixProposal?>? fix,
    Func<T, object?>? reportKey,
    Func<T, string>? detail
) : CompiledRule(descriptor.Id)
{
    public override IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution)
    {
        var candidates = query
            .Evaluate(solution)
            .Where(candidate =>
                candidate
                    is not ICodeElement
                    {
                        Source: { Document.IsGenerated: true }
                            or { Project.Snapshot.IsAnalysisTarget: false }
                    }
            )
            .Select(candidate =>
            {
                solution.CancellationToken.ThrowIfCancellationRequested();
                var element = candidate as ICodeElement;
                if (candidate is AnalysisProject project)
                    element = project
                        .Sources.Where(source => !source.Document.IsGenerated)
                        .Select(source => new CodeFile(source))
                        .FirstOrDefault();
                var span = location is not null
                    ? location(candidate)
                    : element?.Location
                        ?? throw new InvalidOperationException(
                            $"Candidate type '{typeof(T)}' does not expose a source location."
                        );
                var proposal = fix?.Invoke(candidate);
                return (
                    Key: reportKey?.Invoke(candidate),
                    Diagnostic: new RuleDiagnostic(descriptor, span)
                    {
                        Evidence = detail?.Invoke(candidate),
                        Source = element?.Source,
                        Fix = proposal,
                        Fixes = proposal is null ? [] : [proposal],
                    }
                );
            })
            .OrderBy(item => item.Diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Diagnostic.Location.Start)
            .ToArray();
        return reportKey is null
            ? candidates.Select(item => item.Diagnostic)
            : candidates
                .GroupBy(item => (item.Diagnostic.Source?.Project, item.Key))
                .Select(group =>
                    group.First().Diagnostic with
                    {
                        Fixes = Array.AsReadOnly(
                            group.SelectMany(item => item.Diagnostic.Fixes).ToArray()
                        ),
                    }
                );
    }
}
