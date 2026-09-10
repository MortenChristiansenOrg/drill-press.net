namespace DrillPress.Queries;

/// <summary>A custom fact or joined result anchored to an existing source candidate for context-aware reporting and fixes.</summary>
public sealed class LocatedCandidate<T>(T value, ICodeElement anchor) : ICodeElement
{
    /// <summary>The consumer-defined fact being reported.</summary>
    public T Value { get; } = value;

    /// <summary>The anchor's physical location; missing counterparts are reported on their existing owner.</summary>
    public SourceLocation Location { get; } = anchor.Location;

    /// <summary>The anchor's compilation membership.</summary>
    public AnalysisSource? Source { get; } = anchor.Source;
}
