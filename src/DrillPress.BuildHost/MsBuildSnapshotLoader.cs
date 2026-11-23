using System.IO.Abstractions;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.Build.Locator;

namespace DrillPress.BuildHost;

/// <summary>Loads compiler inputs through the installed SDK; SDK targets require restored OS paths.</summary>
public class MsBuildSnapshotLoader
{
    private readonly IFileSystem _fileSystem;
    private readonly SourceFilePolicy _sourcePolicy;

    /// <summary>Creates a loader for local SDK and loose-source targets.</summary>
    public MsBuildSnapshotLoader()
        : this(new FileSystem()) { }

    internal MsBuildSnapshotLoader(IFileSystem fileSystem)
        : this(fileSystem, new FileIdentityProbe()) { }

    internal MsBuildSnapshotLoader(IFileSystem fileSystem, FileIdentityProbe probe)
    {
        _fileSystem = fileSystem;
        _sourcePolicy = new SourceFilePolicy(fileSystem, probe);
    }

    /// <summary>Exports a target using fast compiler capture and default MSBuild properties.</summary>
    public virtual Task<CompilationSnapshot> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken
    ) => LoadAsync(projectPath, new SnapshotLoadOptions(), cancellationToken);

    /// <summary>Exports every evaluated target context with explicit SDK properties and validation policy.</summary>
    public virtual Task<CompilationSnapshot> LoadAsync(
        string target,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken
    ) => LoadAsync([target], options, DisabledProfile(), cancellationToken);

    internal virtual async Task<CompilationSnapshot> LoadAsync(
        IReadOnlyList<string> targets,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    ) => (await LoadCompilationsAsync(targets, options, profile, cancellationToken)).Snapshot;

    /// <summary>Captures live compilations alongside the transport snapshot for conformance and profiling.</summary>
    public Task<SnapshotExport> LoadCompilationsAsync(
        string target,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken = default
    ) => LoadCompilationsAsync([target], options, DisabledProfile(), cancellationToken);

    /// <summary>
    /// Captures several targets in one session, as one snapshot without duplicate contexts. Solution and
    /// project targets load into one workspace, and each keeps the analysis scope it has alone. Loose files and
    /// globs form one loose-source context, without files an analysed project already compiles. MSBuild
    /// properties apply to solution and project targets.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project targets select different .NET SDKs.</exception>
    public Task<SnapshotExport> LoadCompilationsAsync(
        IReadOnlyList<string> targets,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken = default
    ) => LoadCompilationsAsync(targets, options, DisabledProfile(), cancellationToken);

    private async Task<SnapshotExport> LoadCompilationsAsync(
        IReadOnlyList<string> targets,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        if (targets.Count == 0)
            throw new ArgumentException("Specify at least one target.", nameof(targets));
        var resolver = new TargetResolver(_fileSystem);
        var resolved = targets.Select(resolver.Resolve).Distinct(PathComparer).ToArray();
        var loose = resolved
            .Where(IsLooseSource)
            .SelectMany(resolver.ExpandSources)
            .Distinct(PathComparer)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var projects = resolved.Where(target => !IsLooseSource(target)).ToArray();
        if (projects.Length == 0)
            return Restrict(
                await new LooseSourceLoader(_fileSystem).LoadAsync(
                    loose,
                    options,
                    cancellationToken
                ),
                cancellationToken
            );

        var sdk = await SelectSdkAsync(projects, cancellationToken);
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterInstance(sdk);
        }

        var export = await new SdkSnapshotLoader(_fileSystem).LoadAsync(
            projects,
            options,
            sdk.Version.ToString(),
            sdk.MSBuildPath,
            profile,
            cancellationToken
        );
        // An analysed project already compiles these sources with its references and options.
        var compiled = export
            .Snapshot.Projects.Where(project => project.IsAnalysisTarget)
            .SelectMany(project => project.Documents)
            .Select(document => document.Path)
            .ToHashSet(PathComparer);
        loose = loose.Where(path => !compiled.Contains(path)).ToArray();
        if (loose.Length > 0)
        {
            var sources = await new LooseSourceLoader(_fileSystem).LoadAsync(
                loose,
                options with
                {
                    Properties = new(StringComparer.OrdinalIgnoreCase),
                },
                cancellationToken
            );
            var contexts = export
                .Contexts.Concat(sources.Contexts)
                .OrderBy(context => context.Snapshot.ProjectPath, StringComparer.Ordinal)
                .ThenBy(context => context.Snapshot.TargetFramework, StringComparer.Ordinal)
                .ToArray();
            export = new SnapshotExport(
                CompilationSnapshot.Create(contexts.Select(context => context.Snapshot).ToArray()),
                contexts
            );
        }

        return Restrict(export, cancellationToken);
    }

    // Each project directory selects its SDK through global.json; one MSBuild loads them all, so they must agree.
    private async Task<VisualStudioInstance> SelectSdkAsync(
        string[] projects,
        CancellationToken cancellationToken
    )
    {
        var directories = projects
            .Select(project => _fileSystem.Path.GetDirectoryName(project)!)
            .GroupBy(GlobalJson, PathComparer)
            .Select(group => group.First())
            .ToArray();
        var selections = await Task.WhenAll(
            directories.Select(directory =>
                SdkAvailability.ResolveAsync(directory, cancellationToken)
            )
        );
        if (selections.Distinct().Skip(1).Any())
            throw new InvalidOperationException(
                "The targets select different .NET SDKs ("
                    + string.Join(
                        ", ",
                        directories.Zip(
                            selections,
                            (directory, sdk) => $"{sdk.Name} for '{directory}'"
                        )
                    )
                    + "); run them in separate invocations."
            );
        var directory = directories[0];
        var selected = selections[0];
        var version = selected.Version;
        // MSBuild built for a newer .NET can't load into this runtime.
        if (version.Major > Environment.Version.Major)
            throw new NewerRuntimeRequiredException(selected, directory);
        // MSBuildLocator lists other installed SDKs after the selected one, and it skips a selected SDK that this
        // runtime can't load. Loading any other SDK than the one global.json selects would give wrong compilations.
        return MSBuildLocator
                .QueryVisualStudioInstances(
                    new VisualStudioInstanceQueryOptions
                    {
                        DiscoveryTypes = DiscoveryType.DotNetSdk,
                        WorkingDirectory = directory,
                    }
                )
                .FirstOrDefault(instance => instance.Version == version)
            ?? throw new InvalidOperationException(
                $"MSBuild found no .NET SDK {version} for '{projects[0]}', although dotnet selects it there. Run Drill Press with the dotnet installation that holds that SDK."
            );
    }

    // dotnet uses the nearest global.json, so directories sharing it select the same SDK.
    private string GlobalJson(string directory)
    {
        for (
            var current = directory;
            current is not null;
            current = _fileSystem.Path.GetDirectoryName(current)
        )
        {
            var candidate = _fileSystem.Path.Combine(current, "global.json");
            if (_fileSystem.File.Exists(candidate))
                return candidate;
        }

        return "";
    }

    private bool IsLooseSource(string target) =>
        target.IndexOfAny(['*', '?']) >= 0
        || _fileSystem.Path.GetExtension(target).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static PipelineProfile DisabledProfile() => new(false, TextWriter.Null, "build-host");

    private SnapshotExport Restrict(SnapshotExport export, CancellationToken cancellationToken)
    {
        var snapshot = _sourcePolicy.Restrict(export.Snapshot, cancellationToken);
        return new(
            snapshot,
            export
                .Contexts.Zip(
                    snapshot.Projects,
                    (context, project) => new CompilationContext(project, context.Compilation)
                )
                .ToArray()
        );
    }
}
