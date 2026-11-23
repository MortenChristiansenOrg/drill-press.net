using System.IO.Abstractions;
using Microsoft.Build.Construction;

namespace DrillPress.BuildHost;

/// <summary>
/// Decides which projects to load for the resolved solution and project targets of one export, with which global
/// properties, and which loaded projects to analyse. A project target analyses that project, or with
/// <see cref="SnapshotLoadOptions.IncludeReferencedProjects"/> everything it loads. A solution target analyses its
/// projects and the projects they reference. With several targets each one keeps its own scope. MSBuild's project
/// graph accepts a solution only as its sole entry point, so a solution among several targets loads as the projects
/// its selected configuration builds, and every target loads in the one configuration and platform the solutions map
/// their projects to.
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
        Dictionary<string, string> properties,
        bool all,
        HashSet<string> projects,
        HashSet<string> solutionProjects
    )
    {
        _fileSystem = fileSystem;
        EntryPoints = entryPoints;
        Properties = properties;
        _all = all;
        _projects = projects;
        _solutionProjects = solutionProjects;
    }

    /// <summary>The solution or project files to load; a single target loads as given.</summary>
    internal string[] EntryPoints { get; }

    /// <summary>The global properties for every project: the requested ones, with any solution mapping applied.</summary>
    internal Dictionary<string, string> Properties { get; }

    /// <summary>Selects from resolved, distinct solution and project targets. Must run after MSBuild registration.</summary>
    /// <exception cref="InvalidOperationException">The solutions among several targets map projects to more than one configuration or platform.</exception>
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
                options.Properties,
                options.IncludeReferencedProjects || !isProject,
                isProject ? new(_pathComparer) { fileSystem.Path.GetFullPath(target) } : [],
                []
            );
        }

        var projects = new HashSet<string>(_pathComparer);
        var mappings = new List<SolutionProject>();
        foreach (var path in targets.Select(fileSystem.Path.GetFullPath))
        {
            if (IsProject(fileSystem, path))
                projects.Add(path);
            else
                mappings.AddRange(SolutionProjects(fileSystem, path, options.Properties));
        }

        return new ProjectSelection(
            fileSystem,
            projects
                .Concat(mappings.Select(mapping => mapping.Path))
                .Distinct(_pathComparer)
                .ToArray(),
            Configured(options.Properties, mappings),
            options.IncludeReferencedProjects,
            projects,
            mappings.Select(mapping => mapping.Path).ToHashSet(_pathComparer)
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

    // Alone, a solution builds each project in the configuration and platform it maps to. One graph applies one of
    // each to every project, so they must agree; a project reference then binds to the same variant either way.
    private static Dictionary<string, string> Configured(
        Dictionary<string, string> properties,
        List<SolutionProject> mappings
    )
    {
        var variants = mappings
            .DistinctBy(mapping => (mapping.Configuration, mapping.Platform))
            .ToArray();
        if (variants.Length > 1)
            throw new InvalidOperationException(
                "The solutions among the targets build their projects in different configurations ("
                    + string.Join(
                        ", ",
                        variants.Select(mapping =>
                            $"{mapping.Configuration}|{mapping.Platform} for '{mapping.Path}'"
                        )
                    )
                    + "); check such a solution on its own."
            );
        if (variants is not [var variant])
            return properties;
        return new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = variant.Configuration,
            ["Platform"] = variant.Platform,
        };
    }

    // Mirrors MSBuild's project graph: the requested or default solution configuration selects the projects it
    // builds and the configuration and platform of each.
    private static IEnumerable<SolutionProject> SolutionProjects(
        IFileSystem fileSystem,
        string solutionPath,
        Dictionary<string, string> properties
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
        foreach (var project in solution.ProjectsInOrder)
        {
            if (
                project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat
                && project.ProjectConfigurations.TryGetValue(fullName, out var mapping)
                && mapping.IncludeInBuild
            )
                yield return new SolutionProject(
                    fileSystem.Path.GetFullPath(project.AbsolutePath),
                    mapping.ConfigurationName,
                    // Solutions spell the platform of .NET projects "Any CPU"; the projects use "AnyCPU".
                    mapping.PlatformName.Equals("Any CPU", StringComparison.OrdinalIgnoreCase)
                        ? "AnyCPU"
                        : mapping.PlatformName
                );
        }
    }

    private sealed record SolutionProject(string Path, string Configuration, string Platform);
}
