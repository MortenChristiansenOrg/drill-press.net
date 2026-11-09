using System.Collections.Concurrent;

namespace DrillPress;

/// <summary>
/// Decides which loaded source can receive findings, fixes and allowance diagnostics: the analysis target, less any
/// excluded projects and excluded source paths. Queries still select candidates and facts as they would without it,
/// so every loaded context keeps contributing compilations and semantic facts. A scope is immutable, so concurrent
/// evaluations of catalogs with different scopes over one analysis cannot affect each other.
/// </summary>
internal sealed class ReportingScope
{
    private readonly string[] _excludedProjects;
    private readonly string[] _excludedPaths;
    private readonly PathPattern[] _projectPatterns;
    private readonly PathPattern[] _pathPatterns;
    private readonly ConcurrentDictionary<string, bool> _excludedNames = new();
    private readonly ConcurrentDictionary<string, bool> _excludedFiles = new();

    private ReportingScope(string[] excludedProjects, string[] excludedPaths)
    {
        _excludedProjects = excludedProjects;
        _excludedPaths = excludedPaths;
        _projectPatterns = excludedProjects.Select(pattern => new PathPattern(pattern)).ToArray();
        _pathPatterns = excludedPaths.Select(pattern => new PathPattern(pattern)).ToArray();
    }

    /// <summary>Reports in every analysis-target context.</summary>
    public static ReportingScope Targets { get; } = new([], []);

    /// <summary>Also stops reporting in projects whose name matches one of these case-sensitive globs.</summary>
    public ReportingScope ExcludingProjects(IEnumerable<string> patterns) =>
        new(Add(_excludedProjects, patterns), _excludedPaths);

    /// <summary>
    /// Also stops reporting in source files whose full path matches one of these case-sensitive globs. A linked file
    /// is excluded in every project that compiles it.
    /// </summary>
    public ReportingScope ExcludingPaths(IEnumerable<string> patterns) =>
        new(_excludedProjects, Add(_excludedPaths, patterns));

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

    /// <summary>Whether the document's context is included and its path is not excluded.</summary>
    public bool Includes(AnalysisSource source) =>
        Includes(source.Project) && IncludesPath(source.Document.Path);

    /// <summary>Whether no excluded path pattern matches this document path.</summary>
    public bool IncludesPath(string path) =>
        _pathPatterns.Length == 0
        || !_excludedFiles.GetOrAdd(
            path,
            file => _pathPatterns.Any(pattern => pattern.Matches(file))
        );

    private static string[] Add(string[] existing, IEnumerable<string> patterns)
    {
        var added = patterns.ToArray();
        foreach (var pattern in added)
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern, nameof(patterns));
        return [.. existing, .. added.Except(existing)];
    }
}
