namespace DrillPress;

/// <summary>A reportable candidate with a physical location and optional semantic source.</summary>
public interface ICodeElement
{
    /// <summary>The default span reported when a rule supplies no location selector.</summary>
    SourceLocation Location { get; }

    /// <summary>The captured document and compilation, absent for synthetic member candidates.</summary>
    AnalysisSource? Source { get; }
}
