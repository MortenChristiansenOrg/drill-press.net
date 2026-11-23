using System.IO.Abstractions;
using Microsoft.Build.Construction;

namespace DrillPress.BuildHost;

/// <summary>
/// Decides which projects to load for the resolved solution and project targets of one export, and which
/// loaded projects to analyse. A project target analyses that project, or with
/// <see cref="SnapshotLoadOptions.IncludeReferencedProjects"/> everything it loads. A solution target
/// analyses its projects and the projects they reference. With several targets each one keeps its own
/// scope; a solution then loads as the projects its selected configuration builds, without its project
/// configuration mapping, because MSBuild's project graph accepts a solution only as its sole entry point.
/// </summary>
internal sealed class ProjectSelection
{
    private static readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IFileSystem _fileSystem;
    private readonly bool _all;
    private readonly HashSet<string> _projects;
    private readonly HashSet<string> _solutionProjects;

    private ProjectSelection(
        IFileSystem fileSystem,
        string[] entryPoints,
        bool all,
        HashSet<string> projects,
        HashSet<string> solutionProjects
    )
    {
        _fileSystem = fileSystem;
        EntryPoints = entryPoints;
        _all = all;
        _projects = projects;
        _solutionProjects = solutionProjects;
    }

    /// <summary>The solution or project files to load; a single target loads as given.</summary>
    internal string[] EntryPoints { get; }

    /// <summary>Selects from resolved, distinct solution and project targets. Must run after MSBuild registration.</summary>
    internal static ProjectSelection Create(
        IFileSystem fileSystem,
        IReadOnlyList<string> targets,
        SnapshotLoadOptions options
    )
    {
        if (targets is [var target])
        {
            var isProject = IsProject(fileSystem, target);
            return new ProjectSelection(
                fileSystem,
                [target],
                options.IncludeReferencedProjects || !isProject,
                isProject ? new(_pathComparer) { fileSystem.Path.GetFullPath(target) } : [],
                []
            );
        }

        var projects = new HashSet<string>(_pathComparer);
        var solutionProjects = new HashSet<string>(_pathComparer);
        var entryPoints = new List<string>();
        foreach (var path in targets.Select(fileSystem.Path.GetFullPath))
        {
            var isProject = IsProject(fileSystem, path);
            var expanded = isProject
                ? [path]
                : SolutionProjects(fileSystem, path, options.Properties);
            (isProject ? projects : solutionProjects).UnionWith(expanded);
            entryPoints.AddRange(expanded);
        }

        return new ProjectSelection(
            fileSystem,
            entryPoints.Distinct(_pathComparer).ToArray(),
            options.IncludeReferencedProjects,
            projects,
            solutionProjects
        );
    }

    /// <summary>
    /// Whether a target names this project directly or as a solution member, before references are known.
    /// The analysed set can only grow from this by a solution member's references.
    /// </summary>
    internal bool IsNamed(string projectPath)
    {
        var path = _fileSystem.Path.GetFullPath(projectPath);
        return _all || _projects.Contains(path) || _solutionProjects.Contains(path);
    }

    /// <summary>Returns whether to analyse a loaded project, given each loaded project's referenced project paths.</summary>
    internal Func<string, bool> Analysed(ILookup<string, string> references)
    {
        if (_all)
            return _ => true;
        var analysed = new HashSet<string>(_projects, _pathComparer);
        var visited = new HashSet<string>(_pathComparer);
        var pending = new Stack<string>(_solutionProjects);
        while (pending.TryPop(out var path))
        {
            if (!visited.Add(path))
                continue;
            analysed.Add(path);
            foreach (var reference in references[path])
                pending.Push(reference);
        }

        return projectPath => analysed.Contains(_fileSystem.Path.GetFullPath(projectPath));
    }

    private static bool IsProject(IFileSystem fileSystem, string path) =>
        fileSystem.Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    // Mirrors MSBuild's project graph: the requested or default solution configuration selects the projects it builds.
    private static string[] SolutionProjects(
        IFileSystem fileSystem,
        string solutionPath,
        IDictionary<string, string> properties
    )
    {
        var solution = SolutionFile.Parse(solutionPath);
        var configuration =
            properties.TryGetValue("Configuration", out var name) && name.Length > 0
                ? name
                : solution.GetDefaultConfigurationName();
        var platform =
            properties.TryGetValue("Platform", out var selected) && selected.Length > 0
                ? selected
                : solution.GetDefaultPlatformName();
        var fullName = configuration + "|" + platform;
        return solution
            .ProjectsInOrder.Where(project =>
                project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat
                && project.ProjectConfigurations.TryGetValue(fullName, out var mapping)
                && mapping.IncludeInBuild
            )
            .Select(project => fileSystem.Path.GetFullPath(project.AbsolutePath))
            .ToArray();
    }
}
