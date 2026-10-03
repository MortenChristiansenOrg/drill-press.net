using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Whether test evidence establishes execution of an individual occurrence.</summary>
public enum ExecutionCoverage
{
    /// <summary>Evidence is absent, excluded, stale, or cannot distinguish the expression.</summary>
    Unknown,

    /// <summary>A matching instrumented range was not executed.</summary>
    Uncovered,

    /// <summary>A matching instrumented range was executed unambiguously.</summary>
    Covered,
}

internal sealed record CoverageRange(
    TextSpan Span,
    ExecutionCoverage State,
    string FunctionIdentity
);
