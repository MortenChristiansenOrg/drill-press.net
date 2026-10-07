namespace DrillPress;

internal sealed class CandidateRule<T>(
    CodeQuery<T> query,
    RuleDescriptor descriptor,
    Func<T, ReportTarget?>? target,
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
            .SelectMany(candidate =>
                PlannedTarget(candidate) switch
                {
                    null => planning.Projects,
                    { Source: var source } when !IsReportable(source) => [],
                    var report => candidate switch
                    {
                        AnalysisProject project => [project],
                        ICodeElement element => [element.Source.Project],
                        _ => [report.Source.Project],
                    },
                }
            );
    }

    // Unlocated candidates without ReportAt fail later in evaluation; plan every context for them.
    private ReportTarget? PlannedTarget(T candidate) =>
        target?.Invoke(candidate)
        ?? (
            candidate is ICodeElement or AnalysisProject
                ? ReportingLocation.Default(candidate)
                : null
        );

    // Eligibility follows where a finding lands, so ReportAt cannot anchor findings in generated or non-target source.
    private static bool IsReportable(AnalysisSource source) =>
        !source.Document.IsGenerated && source.Project.Snapshot.IsAnalysisTarget;

    public override IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution)
    {
        var candidates = query
            .Evaluate(solution)
            .Select(candidate =>
            {
                solution.CancellationToken.ThrowIfCancellationRequested();
                return (
                    Candidate: candidate,
                    Report: target?.Invoke(candidate) ?? ReportingLocation.Default(candidate)
                );
            })
            .Where(item => IsReportable(item.Report.Source))
            .Select(item =>
            {
                var (candidate, report) = item;
                var proposal = fix?.Invoke(candidate);
                var outcome = failure?.Invoke(candidate);
                return (
                    Key: reportKey?.Invoke(candidate),
                    Diagnostic: new RuleDiagnostic(descriptor, report.Location)
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
                                        Calls = null,
                                    }
                            )
                            .ToArray(),
                        Source = report.Source,
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
