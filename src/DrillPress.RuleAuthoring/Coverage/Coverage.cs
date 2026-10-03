namespace DrillPress;

/// <summary>Requirements backed by test execution collected before analysis.</summary>
public static class Coverage
{
    /// <summary>Requires unambiguous execution evidence for the selected source occurrence. Missing and excluded evidence fails.</summary>
    public static CoverageRequirement Executed { get; } = new(null);

    /// <summary>Counts physical coverable lines, merging matching test runs before calculating percentages.</summary>
    public static LineCoverage Line { get; } = new();
}
