namespace DrillPress;

/// <summary>Declares line thresholds for files, source elements, and evaluated projects.</summary>
public sealed class LineCoverage
{
    internal LineCoverage() { }

    /// <summary>Requires a percentage from zero through one hundred. Missing evidence and zero coverable lines fail.</summary>
    public CoverageRequirement AtLeast(double percentage)
    {
        if (!double.IsFinite(percentage) || percentage < 0 || percentage > 100)
            throw new ArgumentOutOfRangeException(nameof(percentage));
        return new(percentage);
    }
}
