using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Testing;

/// <summary>A unique fixture call ready for an explicitly chosen synthetic execution state.</summary>
public sealed class TestCallCoverage
{
    private readonly TestCoverageFacts _facts;
    private readonly AnalysisSource _source;
    private readonly TextSpan _span;

    internal TestCallCoverage(TestCoverageFacts facts, AnalysisSource source, TextSpan span) =>
        (_facts, _source, _span) = (facts, source, span);

    /// <summary>Supplies synthetic covered evidence; this does not represent real test execution.</summary>
    public TestCoverageFacts Executed() => Set(ExecutionCoverage.Covered, []);

    /// <summary>Supplies a matching synthetic execution point that was not exercised.</summary>
    public TestCoverageFacts NotExecuted() => Set(ExecutionCoverage.Uncovered, []);

    /// <summary>Supplies inconclusive evidence with at least one defined reason, without satisfying an execution requirement.</summary>
    public TestCoverageFacts Unknown(params CoverageReason[] reasons)
    {
        if (reasons.Length == 0 || reasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentException(
                "Unknown evidence requires defined reasons.",
                nameof(reasons)
            );
        return Set(ExecutionCoverage.Unknown, reasons);
    }

    private TestCoverageFacts Set(ExecutionCoverage state, CoverageReason[] reasons)
    {
        _facts.Execution(_source, _span, state, reasons);
        return _facts;
    }
}
