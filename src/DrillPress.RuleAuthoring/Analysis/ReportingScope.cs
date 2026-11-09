namespace DrillPress;

/// <summary>
/// Decides which loaded source can receive findings, fixes and allowance diagnostics. Every loaded
/// context still contributes compilations and semantic facts; only reporting is limited to the
/// analysis target, less any excluded projects.
/// </summary>
internal sealed class ReportingScope
{
    private readonly string[] _excludedProjects;
    private readonly PathPattern[] _projectPatterns;

    private ReportingScope(string[] excludedProjects)
    {
        _excludedProjects = excludedProjects;
        _projectPatterns = excludedProjects.Select(pattern => new PathPattern(pattern)).ToArray();
    }

    /// <summary>Reports in every analysis-target context.</summary>
    public static ReportingScope Targets { get; } = new([]);

    /// <summary>Also stops reporting in projects whose name matches one of these case-sensitive globs.</summary>
    public ReportingScope ExcludingProjects(IEnumerable<string> patterns)
    {
        var added = patterns.ToArray();
        foreach (var pattern in added)
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern, nameof(patterns));
        return new([.. _excludedProjects, .. added.Except(_excludedProjects)]);
    }

    /// <summary>Whether the context belongs to the analysis target and is not excluded.</summary>
    public bool Includes(AnalysisProject project) =>
        project.Snapshot.IsAnalysisTarget
        && !_projectPatterns.Any(pattern => pattern.Matches(project.Name));

    /// <summary>Whether two scopes report in the same contexts.</summary>
    public bool SameAs(ReportingScope other) =>
        _excludedProjects.ToHashSet().SetEquals(other._excludedProjects);
}
