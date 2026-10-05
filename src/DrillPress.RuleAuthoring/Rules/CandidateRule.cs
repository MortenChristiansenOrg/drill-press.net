namespace DrillPress;

internal sealed class CandidateRule<T>(
    CodeQuery<T> query,
    RuleDescriptor descriptor,
    Func<T, SourceLocation>? location,
    Func<T, FixProposal?>? fix,
    Func<T, object?>? reportKey,
    Func<T, string>? detail,
    Func<T, IReadOnlyList<CoverageEvidence>>? coverageFacts,
    Func<T, ConditionFailure>? failure,
    CodeQuery<AnalysisProject>? coverageScope
) : CompiledRule(descriptor.Id)
{
    public override IEnumerable<AnalysisProject> CoverageContexts(AnalysisSolution planning)
    {
        if (coverageScope is not null)
            return coverageScope.In(planning);
        if (!query.RequiresCoverage)
            return [];
        return query
            .Evaluate(planning)
            .Where(IsReportable)
            .SelectMany(candidate =>
                candidate switch
                {
                    AnalysisProject project => [project],
                    ICodeElement { Source: { } source } => new[] { source.Project },
                    _ => planning.Projects,
                }
            );
    }

    private static bool IsReportable(T candidate) =>
        candidate
            is not ICodeElement
            {
                Source: { Document.IsGenerated: true }
                    or { Project.Snapshot.IsAnalysisTarget: false }
            };

    public override IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution)
    {
        var candidates = query
            .Evaluate(solution)
            .Where(IsReportable)
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
                var outcome = failure?.Invoke(candidate);
                return (
                    Key: reportKey?.Invoke(candidate),
                    Diagnostic: new RuleDiagnostic(descriptor, span)
                    {
                        Evidence = detail?.Invoke(candidate),
                        Disposition = outcome?.Disposition ?? FindingDisposition.Violation,
                        OutcomeRemediation = outcome?.Remediation,
                        Coverage = (coverageFacts?.Invoke(candidate) ?? [])
                            .Select(evidence =>
                                solution.Options.ExplainCoverage
                                    ? evidence
                                    : evidence with
                                    {
                                        Ranges = [],
                                    }
                            )
                            .ToArray(),
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
                    group.OrderBy(item => item.Diagnostic.Disposition).First().Diagnostic with
                    {
                        Fixes = Array.AsReadOnly(
                            group.SelectMany(item => item.Diagnostic.Fixes).ToArray()
                        ),
                    }
                );
    }
}
