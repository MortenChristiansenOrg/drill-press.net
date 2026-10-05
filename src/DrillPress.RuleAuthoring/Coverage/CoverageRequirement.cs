using DrillPress.Manifest;

namespace DrillPress;

/// <summary>A coverage requirement usable with the ordinary Require rule declaration.</summary>
public sealed class CoverageRequirement
{
    private readonly double? _minimum;
    private readonly string? _uncoveredMessage;
    private readonly string? _unknownMessage;
    private readonly CoverageReason[] _reviewReasons;

    internal CoverageRequirement(
        double? minimum,
        string? uncoveredMessage = null,
        string? unknownMessage = null,
        CoverageReason[]? reviewReasons = null
    ) =>
        (_minimum, _uncoveredMessage, _unknownMessage, _reviewReasons) = (
            minimum,
            uncoveredMessage,
            unknownMessage,
            reviewReasons?.ToArray() ?? []
        );

    /// <summary>Chooses occurrence-specific remediation for conclusive execution or percentage failures, retaining the stable rule descriptor.</summary>
    public CoverageRequirement OnUncovered(string message) =>
        new(_minimum, Message(message), _unknownMessage, _reviewReasons);

    /// <summary>Chooses occurrence-specific remediation for inconclusive evidence without treating it as satisfied.</summary>
    public CoverageRequirement OnUnknown(string message) =>
        new(_minimum, _uncoveredMessage, Message(message), _reviewReasons);

    /// <summary>Explicitly requests visible, non-gating review only when every unknown reason is a configured unsupported execution mapping. Missing tests, identity/exclusion failures, and operational failures are ineligible.</summary>
    public CoverageRequirement ReviewUnknownFor(params CoverageReason[] reasons)
    {
        if (
            _minimum is not null
            || reasons.Length == 0
            || reasons.Any(reason => reason != CoverageReason.UnsupportedExpressionMapping)
        )
            throw new ArgumentException(
                "Review policy requires explicitly selected unsupported execution-mapping reasons.",
                nameof(reasons)
            );
        return new(_minimum, _uncoveredMessage, _unknownMessage, reasons.Distinct().ToArray());
    }

    private static string Message(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Any(char.IsControl) || message.Contains('\u2028') || message.Contains('\u2029'))
            throw new ArgumentException(
                "Coverage remediation must be a single line.",
                nameof(message)
            );
        return message;
    }

    /// <summary>Reads occurrence evidence after collection; direct rule evaluation without prepared evidence returns unknown.</summary>
    public ExecutionCoverage ExecutionOf(ICodeElement occurrence) =>
        occurrence.Source?.Project.Coverage.ExecutionOf(occurrence) ?? ExecutionCoverage.Unknown;

    /// <summary>Reads source-bound execution evidence and typed explanations after preparation; does not launch tests.</summary>
    public CoverageEvidence Inspect(ICodeElement occurrence) =>
        WithPolicy(
            occurrence.Source?.Project.Coverage.Inspect(occurrence)
                ?? new(
                    ExecutionCoverage.Unknown,
                    Array.AsReadOnly(new[] { CoverageReason.EvidenceNotPrepared }),
                    "",
                    "",
                    ""
                )
        );

    private CoverageEvidence WithPolicy(CoverageEvidence evidence) =>
        _reviewReasons.Length == 0
            ? evidence
            : evidence with
            {
                ReviewReasons = Array.AsReadOnly(_reviewReasons),
            };

    internal ConditionFailure Failure<T>(T candidate)
    {
        var evidence = InspectCandidate(candidate);
        var review =
            evidence.State == ExecutionCoverage.Unknown
            && evidence.Reasons.Count > 0
            && evidence.Reasons.All(_reviewReasons.Contains);
        return new(
            review ? FindingDisposition.Review : FindingDisposition.Violation,
            evidence.State == ExecutionCoverage.Unknown ? _unknownMessage : _uncoveredMessage
        );
    }

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
            MinimumPercentage = _minimum,
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
