namespace DrillPress.Manifest;

/// <summary>Internal bundle result; all loaded contexts must finish before aggregation.</summary>
/// <param name="ProtocolVersion">Exact response contract marker.</param>
/// <param name="RequestId">The originating snapshot request.</param>
/// <param name="Contexts">Complete per-context evaluations.</param>
/// <param name="Batches">Atomic proposed fixes, including affected-context validation.</param>
public sealed record BundleResponse(
    int ProtocolVersion,
    string RequestId,
    ContextEvaluation[] Contexts,
    FixBatch[] Batches
);

/// <summary>Findings from one independently evaluated compilation.</summary>
/// <param name="ContextId">Snapshot compilation identity.</param>
/// <param name="IsComplete">Whether every registered rule completed.</param>
/// <param name="Findings">Exact findings before physical-location aggregation.</param>
public sealed record ContextEvaluation(string ContextId, bool IsComplete, Finding[] Findings);

/// <summary>An exact UTF-16 source location and optional atomic correction.</summary>
/// <param name="RuleId">Single-line stable rule identifier.</param>
/// <param name="Message">The registered single-line remediation.</param>
/// <param name="DocumentId">Document membership within the reporting context.</param>
/// <param name="Start">Zero-based UTF-16 offset.</param>
/// <param name="Length">UTF-16 span length.</param>
/// <param name="BatchId">Proposed batch, or null for a finding without a correction.</param>
public sealed record Finding(
    string RuleId,
    string Message,
    string DocumentId,
    int Start,
    int Length,
    string? BatchId
);

/// <summary>All edits for a correction; conflicts withhold the entire batch.</summary>
/// <param name="Id">Response-local batch identity.</param>
/// <param name="Edits">Complete set of exact source replacements.</param>
/// <param name="Validations">Safety decisions from every affected loaded context.</param>
public sealed record FixBatch(string Id, SourceEdit[] Edits, FixValidation[] Validations);

/// <summary>Replacement tied to captured original bytes and text.</summary>
/// <param name="FileIdentity">Canonical editable file identity in the snapshot.</param>
/// <param name="Fingerprint">Expected original-byte SHA-256.</param>
/// <param name="Start">Zero-based UTF-16 offset.</param>
/// <param name="Length">UTF-16 span length.</param>
/// <param name="OriginalText">Exact text occupying the span.</param>
/// <param name="Replacement">Replacement text.</param>
public sealed record SourceEdit(
    string FileIdentity,
    string Fingerprint,
    int Start,
    int Length,
    string OriginalText,
    string Replacement
);

/// <summary>Binding and active-source validation for an entire batch in one context.</summary>
/// <param name="ContextId">Affected snapshot compilation.</param>
/// <param name="IsSafe">False for inactive source, ambiguous binding, or a semantic change.</param>
public sealed record FixValidation(string ContextId, bool IsSafe);

/// <summary>A physical finding after complete-context agreement and conflict filtering.</summary>
/// <param name="RuleId">Stable diagnostic identifier.</param>
/// <param name="Message">Registered remediation.</param>
/// <param name="FileIdentity">Canonical source identity.</param>
/// <param name="Path">Display source path.</param>
/// <param name="Start">UTF-16 source offset.</param>
/// <param name="Length">UTF-16 span length.</param>
/// <param name="Line">One-based physical line.</param>
/// <param name="Column">One-based physical UTF-16 column.</param>
/// <param name="BatchId">Retained safe batch, or null.</param>
public sealed record AggregatedFinding(
    string RuleId,
    string Message,
    string FileIdentity,
    string Path,
    int Start,
    int Length,
    int Line,
    int Column,
    string? BatchId
);

/// <summary>Validated diagnostics and the same conflict-filtered plan used for automatic writes.</summary>
/// <param name="Findings">Aggregated physical diagnostics.</param>
/// <param name="Batches">Retained whole batches.</param>
/// <param name="Edits">Deduplicated replacements from retained batches.</param>
public sealed record ValidatedResult(
    AggregatedFinding[] Findings,
    FixBatch[] Batches,
    SourceEdit[] Edits
);
