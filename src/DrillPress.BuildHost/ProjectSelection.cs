using System.IO.Abstractions;

namespace DrillPress.BuildHost;

/// <summary>
/// Decides which loaded projects to analyse for the resolved targets of one export: one solution, or distinct
/// projects. A solution analyses everything it loads. A project target analyses that project, or with
/// <see cref="SnapshotLoadOptions.IncludeReferencedProjects"/> everything it loads.
/// </summary>
internal sealed class ProjectSelection
{
    private static readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IFileSystem _fileSystem;
    private readonly HashSet<string>? _projects;

    private ProjectSelection(IFileSystem fileSystem, HashSet<string>? projects)
    {
        _fileSystem = fileSystem;
        _projects = projects;
    }

    /// <summary>Selects from one solution target or distinct project targets.</summary>
    internal static ProjectSelection Create(
        IFileSystem fileSystem,
        IReadOnlyList<string> targets,
        SnapshotLoadOptions options
    ) =>
        new(
            fileSystem,
            options.IncludeReferencedProjects
            || !targets.All(target => IsProject(fileSystem, target))
                ? null
                : targets.Select(fileSystem.Path.GetFullPath).ToHashSet(_pathComparer)
        );

    /// <summary>Whether to analyse a loaded project.</summary>
    internal bool IsAnalysed(string projectPath) =>
        _projects is null || _projects.Contains(_fileSystem.Path.GetFullPath(projectPath));

    private static bool IsProject(IFileSystem fileSystem, string path) =>
        fileSystem.Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase);
}
