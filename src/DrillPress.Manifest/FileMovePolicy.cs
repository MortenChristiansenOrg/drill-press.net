namespace DrillPress.Manifest;

/// <summary>
/// Decides from captured project membership alone whether a source file can move without changing which
/// loaded projects compile it. Only files that the SDK's default compile glob selects in every project
/// containing them can move, and only to a path every one of those projects still selects and no other
/// loaded project would.
/// </summary>
internal static class FileMovePolicy
{
    private const string LooseContextId = "loose";

    // Names Roslyn and the snapshot capture classify as generated code.
    private static readonly string[] _generatedSuffixes =
    [
        ".g.cs",
        ".g.i.cs",
        ".generated.cs",
        ".designer.cs",
    ];

    /// <summary>Returns why moving the file to the destination is unsupported, or null when every loaded project keeps the same membership.</summary>
    internal static string? Check(
        IReadOnlyList<ProjectSnapshot> projects,
        string fileIdentity,
        string destination
    )
    {
        var memberships = projects
            .Select(project =>
                (
                    Project: project,
                    Document: project.Documents.FirstOrDefault(document =>
                        document.FileIdentity == fileIdentity
                    )
                )
            )
            .Where(membership => membership.Document is not null)
            .ToArray();
        if (memberships.Length == 0)
            return "the file is not part of a loaded project";
        var document = memberships[0].Document!;
        if (document.IsGenerated || !document.IsEditable)
            return "the file is generated or not editable";
        if (destination == document.Path)
            return "the file already has that path";
        if (InvalidDestination(destination) is { } invalid)
            return invalid;
        foreach (var (project, membership) in memberships)
        {
            if (project.CompileGlob is not { } glob)
                return $"{Describe(project)} does not select its sources only by the SDK's default compile glob";
            if (!membership!.IsDefaultCompileItem)
                return $"{Describe(project)} compiles the file through a linked or explicit Compile item";
            if (!Includes(glob, destination))
                return $"{Describe(project)} would not compile {SourcePaths.FileName(destination)} at its new path";
        }

        foreach (
            var project in projects.Where(project =>
                project.ContextId != LooseContextId
                && project.ProjectPath.Length > 0
                && !memberships.Any(membership => membership.Project == project)
            )
        )
        {
            if (
                project.CompileGlob is { } glob
                    ? Includes(glob, destination)
                    : SourcePaths.IsBelow(destination, SourcePaths.Directory(project.ProjectPath))
            )
                return $"{Describe(project)} would also compile the file at its new path";
        }

        return projects
            .SelectMany(project => project.Documents)
            .Any(other =>
                other.FileIdentity != fileIdentity
                && string.Equals(
                    SourcePaths.Normalize(other.Path),
                    SourcePaths.Normalize(destination),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ? "a loaded file already has that path"
            : null;
    }

    private static string? InvalidDestination(string destination)
    {
        if (!SourcePaths.IsNormalized(destination))
            return "the new path is not a normalized file path";
        var name = SourcePaths.FileName(destination);
        if (!name.EndsWith(".cs", StringComparison.Ordinal) || name.Length <= ".cs".Length)
            return "the new file name does not end in .cs";
        if (
            _generatedSuffixes.Any(suffix =>
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ) || name.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase)
        )
            return "the new file name marks generated code";
        return null;
    }

    // The default glob compiles every .cs file below the project directory except in excluded directories,
    // excluded folder names, obj folders and folders whose name starts with a dot; a dot-named file is
    // withheld as well.
    private static bool Includes(CompileGlobSnapshot glob, string path) =>
        SourcePaths.IsBelow(path, glob.Directory)
        && !SourcePaths
            .Segments(SourcePaths.Relative(path, glob.Directory))
            .Any(segment =>
                segment.StartsWith('.')
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || glob.ExcludedFolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase)
            )
        && !glob.ExcludedDirectories.Any(excluded =>
            SourcePaths.IsBelow(path, excluded, ignoreCase: true)
        );

    private static string Describe(ProjectSnapshot project) =>
        project.TargetFramework.Length > 0
            ? $"{project.Name} ({project.TargetFramework})"
            : project.Name;
}
