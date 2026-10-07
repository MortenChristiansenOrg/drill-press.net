namespace DrillPress;

/// <summary>A reportable candidate: an original source membership and the span reported by default.</summary>
public interface ICodeElement
{
    /// <summary>The default span reported when a clause does not choose another with ReportAt.</summary>
    SourceLocation Location { get; }

    /// <summary>The captured document and its evaluated compilation.</summary>
    AnalysisSource Source { get; }
}
