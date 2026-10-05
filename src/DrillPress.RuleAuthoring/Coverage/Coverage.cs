namespace DrillPress;

/// <summary>Requirements backed by test execution collected before analysis.</summary>
public static class Coverage
{
    /// <summary>Requires unambiguous execution evidence for the selected source occurrence. Missing and excluded evidence fails.</summary>
    public static CoverageRequirement Executed { get; } = new(null);

    /// <summary>Requires a compiler-verified advancement point for a CodeEnumeration. Empty results can satisfy this; acquisition and body coverage cannot substitute for it.</summary>
    public static CoverageRequirement EnumerationStarted { get; } =
        new(null, enumerationStarted: true);

    /// <summary>Counts physical coverable lines, merging matching test runs before calculating percentages.</summary>
    public static LineCoverage Line { get; } = new();
}
