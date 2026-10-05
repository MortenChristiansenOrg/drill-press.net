using DrillPress.Manifest;

namespace DrillPress;

/// <summary>A coverage requirement usable with the ordinary Require rule declaration.</summary>
public sealed class CoverageRequirement
{
    private readonly double? _minimum;

    internal CoverageRequirement(double? minimum) => _minimum = minimum;

    /// <summary>Reads occurrence evidence after collection; direct rule evaluation without prepared evidence returns unknown.</summary>
    public ExecutionCoverage ExecutionOf(ICodeElement occurrence) =>
        occurrence.Source?.Project.Coverage.ExecutionOf(occurrence) ?? ExecutionCoverage.Unknown;

    /// <summary>Reads source-bound execution evidence and typed explanations after preparation; does not launch tests.</summary>
    public CoverageEvidence Inspect(ICodeElement occurrence) =>
        occurrence.Source?.Project.Coverage.Inspect(occurrence)
        ?? new(
            ExecutionCoverage.Unknown,
            Array.AsReadOnly(new[] { CoverageReason.EvidenceNotPrepared }),
            "",
            "",
            ""
        );

    internal CoverageEvidence InspectCandidate<T>(T candidate)
    {
        if (_minimum is null)
            return candidate is ICodeElement element
                ? Inspect(element)
                : new(
                    ExecutionCoverage.Unknown,
                    Array.AsReadOnly(new[] { CoverageReason.EvidenceNotPrepared }),
                    "",
                    "",
                    ""
                );
        var measured = Coverage.Line.Measure(candidate);
        var project = candidate is AnalysisProject selected
            ? selected
            : (candidate as ICodeElement)?.Source?.Project;
        return new(
            measured.IsComplete && measured.Coverable > 0
                ? measured.Covered == measured.Coverable
                    ? ExecutionCoverage.Covered
                    : ExecutionCoverage.Uncovered
                : ExecutionCoverage.Unknown,
            measured.Reasons,
            project?.Name ?? "",
            project?.TargetFramework ?? "",
            project?.Snapshot.ContextId ?? ""
        )
        {
            Metric = CoverageMetric.Line,
            Lines = measured,
        };
    }

    internal bool Satisfied<T>(T candidate) =>
        _minimum is null
            ? candidate is ICodeElement element && ExecutionOf(element) == ExecutionCoverage.Covered
            : Coverage.Line.Measure(candidate) is { IsComplete: true, Coverable: > 0 } measurement
                && measurement.Percentage >= _minimum;

    internal string Detail<T>(T candidate) =>
        CoverageEvidenceFormatter.Format(InspectCandidate(candidate), _minimum);
}
