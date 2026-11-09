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
    public override void Prepare(AnalysisSolution solution) => _ = query.In(solution);

    public override IEnumerable<AnalysisProject> CoverageContexts(
        AnalysisSolution planning,
        ReportingScope reporting
    )
    {
        // An explicit scope collects only for projects with source the reporting scope includes.
        if (coverageScope is not null)
            return coverageScope
                .In(planning)
                .Where(project =>
                    project.Sources.Any(source =>
                        !source.Document.IsGenerated && reporting.Includes(source)
                    )
                );
        if (!query.RequiresCoverage)
            return [];
        return query
            .Evaluate(planning)
            .SelectMany(candidate =>
                (target?.Invoke(candidate), candidate) switch
                {
                    // Unlocated candidates without ReportAt fail later in evaluation; plan every context for them.
                    (null, not (ICodeElement or AnalysisProject)) => planning.Projects,
                    var (report, _) => (report ?? DefaultTarget(candidate, reporting)) switch
                    {
                        null => [],
                        { Source: var source } when !IsReportable(reporting, source) => [],
                        var located => candidate switch
                        {
                            AnalysisProject project => [project],
                            ICodeElement element => [element.Source.Project],
                            _ => [located.Source.Project],
                        },
                    },
                }
            );
    }

    private ReportTarget? Target(T candidate, ReportingScope reporting) =>
        target?.Invoke(candidate) ?? DefaultTarget(candidate, reporting);

    // A project reports at its first ordinary source the scope includes. Null for a project the scope leaves out, or
    // whose ordinary sources are all excluded, so an excluded project needs no ordinary source to report at.
    private static ReportTarget? DefaultTarget(T candidate, ReportingScope reporting) =>
        candidate switch
        {
            AnalysisProject project when !reporting.Includes(project) => null,
            AnalysisProject project
                when project.Sources.Any(source => !source.Document.IsGenerated) => project
                .Sources.Where(source => !source.Document.IsGenerated && reporting.Includes(source))
                .Select(source => new ReportTarget(source.Locate(new(0, 0)), source))
                .FirstOrDefault(),
            _ => ReportingLocation.Default(candidate),
        };

    // Eligibility follows where a finding lands, so ReportAt cannot anchor findings in generated, non-target or
    // excluded source.
    private static bool IsReportable(ReportingScope reporting, AnalysisSource source) =>
        !source.Document.IsGenerated && reporting.Includes(source);

    public override IEnumerable<RuleDiagnostic> Evaluate(
        AnalysisSolution solution,
        ReportingScope reporting
    )
    {
        var candidates = query
            .Evaluate(solution)
            .Select(candidate =>
            {
                solution.CancellationToken.ThrowIfCancellationRequested();
                return (Candidate: candidate, Report: Target(candidate, reporting));
            })
            .Where(item => item.Report is not null && IsReportable(reporting, item.Report.Source))
            .Select(item =>
            {
                var candidate = item.Candidate;
                var report = item.Report!;
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
