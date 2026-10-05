namespace DrillPress;

/// <summary>Identifies an exact physical source span and its human-readable coordinates.</summary>
/// <param name="FilePath">The physical source file containing the span.</param>
/// <param name="Start">The zero-based character offset of the span.</param>
/// <param name="Length">The span length in characters.</param>
/// <param name="Line">The one-based source line.</param>
/// <param name="Column">The one-based source column.</param>
public sealed record SourceLocation(string FilePath, int Start, int Length, int Line, int Column);

/// <summary>Defines the stable identifier and remediation text presented for a rule.</summary>
/// <param name="Id">The stable rule identifier.</param>
/// <param name="Message">Concise guidance for correcting a violation.</param>
public sealed record RuleDescriptor(string Id, string Message)
{
    /// <summary>Optional author estimate of agent effort for a typical fix; null leaves the rule unclassified.</summary>
    public RuleFixComplexity? FixComplexity { get; init; }
}

/// <summary>Associates a rule violation with its physical source location.</summary>
/// <param name="Descriptor">The rule that produced the violation.</param>
/// <param name="Location">The violating source expression.</param>
public sealed record RuleDiagnostic(RuleDescriptor Descriptor, SourceLocation Location)
{
    /// <summary>Optional single-line occurrence evidence, separate from the stable remediation shared by a rule.</summary>
    public string? Evidence { get; init; }

    /// <summary>Typed coverage facts for this occurrence, retaining distinct evaluated contexts and requirement kinds.</summary>
    public IReadOnlyList<CoverageEvidence> Coverage { get; init; } = [];

    /// <summary>Document membership used for context-specific aggregation.</summary>
    public AnalysisSource? Source { get; init; }

    /// <summary>An optional complete correction and its cross-context proof.</summary>
    public FixProposal? Fix { get; init; }

    /// <summary>All proposals retained by a reporting group, including candidates whose displayed diagnostics were deduplicated. The engine validates their complete union and conflicts before offering a correction.</summary>
    public IReadOnlyList<FixProposal> Fixes { get; init; } = [];
}
