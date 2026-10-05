namespace DrillPress.Testing;

/// <summary>A unique fixture document ready for synthetic file/project line measurements.</summary>
public sealed class TestFileCoverage
{
    private readonly TestCoverageFacts _facts;
    private readonly AnalysisSource _source;

    internal TestFileCoverage(TestCoverageFacts facts, AnalysisSource source) =>
        (_facts, _source) = (facts, source);

    /// <summary>Supplies counts and completeness independently. Zero coverable lines and incomplete measurements remain unknown, even at a zero threshold.</summary>
    public TestCoverageFacts Lines(
        int covered,
        int coverable,
        bool isComplete = true,
        params CoverageReason[] reasons
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(covered);
        ArgumentOutOfRangeException.ThrowIfLessThan(coverable, covered);
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentException("Line evidence requires defined reasons.", nameof(reasons));
        var supplied = reasons.ToHashSet();
        if (!isComplete)
            supplied.Add(CoverageReason.IncompleteLineEvidence);
        if (coverable == 0)
            supplied.Add(CoverageReason.ZeroCoverableLines);
        _facts.Lines(
            _source,
            new(covered, coverable, isComplete, Array.AsReadOnly(supplied.Order().ToArray()))
        );
        return _facts;
    }
}
