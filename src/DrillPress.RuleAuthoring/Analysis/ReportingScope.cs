using System.Collections.Concurrent;

namespace DrillPress;

/// <summary>
/// Decides which loaded source can receive findings, fixes and allowance diagnostics: the analysis target, less any
/// excluded projects. Queries still select candidates and facts as they would without it, so every loaded context
/// keeps contributing compilations and semantic facts. A scope is immutable, so concurrent evaluations of catalogs
/// with different scopes over one analysis cannot affect each other.
/// </summary>
internal sealed class ReportingScope
{
    private readonly string[] _excludedProjects;
    private readonly PathPattern[] _projectPatterns;
    private readonly ConcurrentDictionary<string, bool> _excludedNames = new();

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
        && (
            _projectPatterns.Length == 0
            || !_excludedNames.GetOrAdd(
                project.Name,
                name => _projectPatterns.Any(pattern => pattern.Matches(name))
            )
        );
}
