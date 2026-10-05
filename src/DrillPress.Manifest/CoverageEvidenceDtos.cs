namespace DrillPress;

/// <summary>Whether a visible finding fails the check or requests review under an explicit coverage policy.</summary>
public enum FindingDisposition
{
    /// <summary>Fails the check; this is the default for unknown and uncovered requirements.</summary>
    Violation,

    /// <summary>Remains visible without failing the check; it proves no coverage.</summary>
    Review,
}

/// <summary>Whether prepared test evidence establishes execution of a selected source occurrence.</summary>
public enum ExecutionCoverage
{
    /// <summary>Evidence is missing, stale, excluded, or inconclusive.</summary>
    Unknown,

    /// <summary>A matching execution point was not exercised.</summary>
    Uncovered,

    /// <summary>Execution is established by matching unambiguous evidence.</summary>
    Covered,
}

/// <summary>Provider-independent explanations for inconclusive execution or line evidence.</summary>
public enum CoverageReason
{
    /// <summary>Direct evaluation has no prepared test evidence.</summary>
    EvidenceNotPrepared,

    /// <summary>The source has no reproducible on-disk project context.</summary>
    LooseSource,

    /// <summary>No discovered test project references this evaluated target.</summary>
    NoApplicableTests,

    /// <summary>The build has no readable associated portable symbols.</summary>
    MissingSymbols,

    /// <summary>The build's portable symbols do not match the captured compilation.</summary>
    SymbolIdentityMismatch,

    /// <summary>Matching report modules are missing or ambiguous.</summary>
    ModuleIdentityMismatch,

    /// <summary>The report's document checksum does not match the captured source.</summary>
    SourceIdentityMismatch,

    /// <summary>The document is absent from the report, possibly due to collector exclusions.</summary>
    MissingOrExcludedDocument,

    /// <summary>No instrumented range identifies the selected source occurrence.</summary>
    MissingRange,

    /// <summary>At least one matching range has partial or inconclusive execution evidence.</summary>
    PartialRange,

    /// <summary>A covered range is too broad to prove this selected expression executed.</summary>
    UnsupportedExpressionMapping,

    /// <summary>Some requested source documents have no usable line evidence.</summary>
    IncompleteLineEvidence,

    /// <summary>The selected scope contains no measured coverable lines.</summary>
    ZeroCoverableLines,

    /// <summary>A report range lacks valid coordinates or function identity.</summary>
    InvalidReportRange,

    /// <summary>The compiler loop has no verified, separately instrumented advancement condition.</summary>
    UnsupportedEnumerationMapping,
}

/// <summary>The source fact measured by a coverage requirement.</summary>
public enum CoverageMetric
{
    /// <summary>Execution of a selected source occurrence.</summary>
    Execution,

    /// <summary>Physical fully covered and coverable lines.</summary>
    Line,

    /// <summary>An enumerator-based loop reached its first MoveNext or MoveNextAsync attempt, independently of collection evaluation or body entry.</summary>
    Enumeration,
}

/// <summary>A source-relative instrumented range; it is evidence, not a claim that every enclosed expression executed.</summary>
/// <param name="Start">Zero-based UTF-16 source offset.</param>
/// <param name="Length">UTF-16 range length.</param>
/// <param name="State">Merged execution evidence for this range.</param>
public sealed record CoverageRangeEvidence(int Start, int Length, ExecutionCoverage State);

/// <summary>Measured line counts with completeness retained separately from the percentage.</summary>
/// <param name="Covered">Fully covered physical lines.</param>
/// <param name="Coverable">Measured physical coverable lines.</param>
/// <param name="IsComplete">Whether all requested documents supplied usable evidence.</param>
/// <param name="Reasons">Typed explanations for incomplete or zero-coverable evidence.</param>
public sealed record LineCoverageMeasurement(
    int Covered,
    int Coverable,
    bool IsComplete,
    IReadOnlyList<CoverageReason> Reasons
)
{
    /// <summary>The measured count ratio; zero coverable lines produce zero without implying a satisfied requirement.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double Percentage => Coverable == 0 ? 0 : 100.0 * Covered / Coverable;
}

/// <summary>Read-only execution or line evidence tied to one independently evaluated project context.</summary>
/// <param name="State">Established execution state, or full/partial line state for line measurements.</param>
/// <param name="Reasons">Typed explanations; an empty list denotes conclusive evidence.</param>
/// <param name="Project">Evaluated project name.</param>
/// <param name="Framework">Evaluated target framework.</param>
/// <param name="ContextId">Independent compilation membership.</param>
public sealed record CoverageEvidence(
    ExecutionCoverage State,
    IReadOnlyList<CoverageReason> Reasons,
    string Project,
    string Framework,
    string ContextId
)
{
    /// <summary>The kind of source evidence; execution is the ordinary occurrence default.</summary>
    public CoverageMetric Metric { get; init; }

    /// <summary>Line counts and completeness when this evidence represents a line requirement.</summary>
    public LineCoverageMeasurement? Lines { get; init; }

    /// <summary>Matching source ranges, supplied by SDK inspection or opt-in detailed bundle output.</summary>
    public IReadOnlyList<CoverageRangeEvidence> Ranges { get; init; } = [];

    /// <summary>Explicit reasons eligible for review-only gating; this is policy metadata and never changes State.</summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public IReadOnlyList<CoverageReason>? ReviewReasons { get; init; }

    /// <summary>The required percentage when this fact describes a line requirement.</summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public double? MinimumPercentage { get; init; }

    /// <summary>Whether the measured fact satisfies its execution or line requirement; unknown evidence always returns false.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SatisfiesRequirement =>
        State != ExecutionCoverage.Unknown
        && (
            Metric == CoverageMetric.Line
                ? Lines is { IsComplete: true, Coverable: > 0 }
                    && MinimumPercentage is { } minimum
                    && Lines.Percentage >= minimum
                : State == ExecutionCoverage.Covered
        );

    /// <summary>Whether every unknown reason is explicitly eligible for review; this does not satisfy the requirement.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsReviewEligible =>
        Metric == CoverageMetric.Execution
        && State == ExecutionCoverage.Unknown
        && Reasons.Count > 0
        && ReviewReasons is { Count: > 0 }
        && Reasons.All(reason =>
            reason == CoverageReason.UnsupportedExpressionMapping && ReviewReasons.Contains(reason)
        );

    /// <summary>Whether refreshed collection might repair stale, missing, or partial evidence; unsupported mappings and absent tests need another remedy.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RefreshMayHelp =>
        Reasons.Any(reason =>
            reason
                is CoverageReason.MissingSymbols
                    or CoverageReason.SymbolIdentityMismatch
                    or CoverageReason.ModuleIdentityMismatch
                    or CoverageReason.SourceIdentityMismatch
                    or CoverageReason.MissingOrExcludedDocument
                    or CoverageReason.PartialRange
        );
}
