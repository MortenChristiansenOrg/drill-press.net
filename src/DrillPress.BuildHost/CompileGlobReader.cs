using System.IO.Abstractions;
using DrillPress.Manifest;
using Microsoft.Build.Construction;

namespace DrillPress.BuildHost;

/// <summary>
/// Reads whether an evaluated project selects its C# sources only through the SDK's default compile glob,
/// and which directories that glob leaves out. Any other Compile item element outside the SDK, including an
/// explicit include, a removal or a metadata update in evaluation and a removal or wildcard include in a
/// target, or an exclusion pattern this reader cannot reduce to a directory or to file names no source can
/// have, makes the membership unknown. It also reads where any Compile wildcard of a project could reach.
/// </summary>
internal sealed class CompileGlobReader(IFileSystem fileSystem)
{
    private const string DefaultItemsFile = "Microsoft.NET.Sdk.DefaultItems.props";
    private const string DefaultInclude = "**/*$(DefaultLanguageSourceExtension)";
    private const string HiddenFolders = "**/.*/**";

    private readonly IFileSystem _fileSystem = fileSystem;

    /// <summary>The glob and the full paths of the items it selected, or null when the membership is unknown.</summary>
    internal CompileGlobMembership? Read(Microsoft.Build.Evaluation.Project project)
    {
        if (
            !IsTrue(project.GetPropertyValue("EnableDefaultItems"))
            || !IsTrue(project.GetPropertyValue("EnableDefaultCompileItems"))
            || project.GetPropertyValue("DefaultLanguageSourceExtension") != ".cs"
            || CompileElements(project) is not ({ } glob, var removals)
        )
            return null;
        var directory = project.DirectoryPath;
        var excluded = new List<string>();
        var folders = new List<string>();
        foreach (
            var fragment in removals
                .Prepend(glob.Exclude)
                .SelectMany(patterns =>
                    project
                        .ExpandString(patterns)
                        .Split(
                            ';',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                        )
                )
        )
        {
            if (!Exclude(fragment.Replace('\\', '/'), directory, excluded, folders))
                return null;
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var items = project
            .GetItems("Compile")
            .Where(item => item.Xml == glob && item.GetMetadataValue("Link").Length == 0)
            .Select(item => _fileSystem.Path.GetFullPath(item.EvaluatedInclude, directory))
            .ToHashSet(comparer);
        return new(
            new CompileGlobSnapshot(directory, excluded.Distinct(comparer).ToArray())
            {
                ExcludedFolderNames = folders.Distinct(comparer).ToArray(),
            },
            items
        );
    }

    /// <summary>The directories below which any Compile wildcard of the project, in evaluation or in a target, could select a file.</summary>
    internal string[] Roots(Microsoft.Build.Evaluation.Project project)
    {
        var directory = project.DirectoryPath;
        return project
            .GetLogicalProject()
            .OfType<ProjectItemElement>()
            .Where(element =>
                element.ItemType.Equals("Compile", StringComparison.OrdinalIgnoreCase)
                && element.Include.Length > 0
            )
            .SelectMany(element =>
                project
                    .ExpandString(element.Include)
                    .Split(
                        ';',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
            )
            .Select(fragment => fragment.Replace('\\', '/'))
            .Where(HasWildcard)
            .Select(fragment =>
            {
                var prefix = fragment[..fragment.IndexOfAny(['*', '?'])];
                var folder = prefix[..(prefix.LastIndexOf('/') + 1)];
                return _fileSystem
                    .Path.GetFullPath(folder.Length == 0 ? "." : folder, directory)
                    .TrimEnd('/', '\\');
            })
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // Records a directory or a folder name at any depth the pattern excludes, or accepts a pattern no .cs
    // file can match; false when the pattern could exclude individual sources.
    private bool Exclude(
        string pattern,
        string directory,
        List<string> excluded,
        List<string> folders
    )
    {
        if (pattern == HiddenFolders)
            return true;
        if (
            pattern.StartsWith("**/", StringComparison.Ordinal)
            && pattern.EndsWith("/**", StringComparison.Ordinal)
            && pattern.Length > "**//**".Length
            && pattern[3..^3] is var folder
            && !folder.Contains('/')
            && !HasWildcard(folder)
        )
        {
            folders.Add(folder);
            return true;
        }
        if (pattern.EndsWith("/**", StringComparison.Ordinal) && !HasWildcard(pattern[..^3]))
        {
            var prefix = pattern[..^3];
            excluded.Add(
                _fileSystem
                    .Path.GetFullPath(prefix.Length == 0 ? "/" : prefix, directory)
                    .TrimEnd('/', '\\')
            );
            return true;
        }
        if (!HasWildcard(pattern))
            return !pattern.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        return pattern.StartsWith("**/", StringComparison.Ordinal)
            && pattern[3..] is var name
            && !name.Contains('/')
            && CannotNameSource(name);
    }

    // The SDK's single default Compile element and the SDK's own removals, such as the Web SDK's wwwroot,
    // when no other Compile element adds, removes or updates items in evaluation; an SDK element that only
    // updates metadata is allowed. In targets, the SDK's own elements and other elements that only update
    // metadata or add files without wildcards, such as a generated entry point, are allowed.
    private (ProjectItemElement? Glob, List<string> Removals) CompileElements(
        Microsoft.Build.Evaluation.Project project
    )
    {
        var sdk = project.GetPropertyValue("MSBuildExtensionsPath");
        sdk = sdk.Length == 0 ? "" : _fileSystem.Path.GetFullPath(sdk);
        ProjectItemElement? glob = null;
        var removals = new List<string>();
        foreach (var element in project.GetLogicalProject().OfType<ProjectItemElement>())
        {
            if (!element.ItemType.Equals("Compile", StringComparison.OrdinalIgnoreCase))
                continue;
            var file = element.ContainingProject.FullPath;
            var inSdk = sdk.Length > 0 && file.StartsWith(sdk, StringComparison.OrdinalIgnoreCase);
            if (InTarget(element))
            {
                if (
                    inSdk
                    || element.Remove.Length == 0
                        && !HasWildcard(project.ExpandString(element.Include))
                )
                    continue;
                return (null, removals);
            }
            if (
                _fileSystem.Path.GetFileName(file) == DefaultItemsFile
                && element.Include == DefaultInclude
                && glob is null
            )
            {
                glob = element;
                continue;
            }
            if (inSdk && element.Include.Length == 0)
            {
                if (element.Remove.Length > 0)
                    removals.Add(element.Remove);
                continue;
            }
            return (null, removals);
        }
        return (glob, removals);
    }

    private static bool InTarget(ProjectElement element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
            if (parent is ProjectTargetElement)
                return true;
        return false;
    }

    // A file-name pattern that cannot match a name ending in .cs, such as *.user or .DS_Store.
    private static bool CannotNameSource(string name)
    {
        var wildcard = name.LastIndexOfAny(['*', '?']);
        var tail = name[(wildcard + 1)..];
        return wildcard < 0
            ? !name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            : tail.Length > 0
                && !tail.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !".cs".EndsWith(tail, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasWildcard(string text) => text.IndexOfAny(['*', '?']) >= 0;

    private static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase);
}
