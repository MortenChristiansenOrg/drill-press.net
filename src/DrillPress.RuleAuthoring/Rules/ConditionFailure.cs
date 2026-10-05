namespace DrillPress;

internal sealed record ConditionFailure(
    FindingDisposition Disposition = FindingDisposition.Violation,
    string? Remediation = null
);
