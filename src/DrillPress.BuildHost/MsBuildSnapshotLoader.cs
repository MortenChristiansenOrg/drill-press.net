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
    ) => LoadAsync(target, options, DisabledProfile(), cancellationToken);

    internal virtual async Task<CompilationSnapshot> LoadAsync(
        string target,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    ) => (await LoadCompilationsAsync(target, options, profile, cancellationToken)).Snapshot;

    /// <summary>Captures live compilations alongside the transport snapshot for conformance and profiling.</summary>
    public Task<SnapshotExport> LoadCompilationsAsync(
        string target,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken = default
    ) => LoadCompilationsAsync(target, options, DisabledProfile(), cancellationToken);

    private async Task<SnapshotExport> LoadCompilationsAsync(
        string target,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        var resolver = new TargetResolver(_fileSystem);
        target = resolver.Resolve(target);
        if (
            target.IndexOfAny(['*', '?']) >= 0
            || _fileSystem
                .Path.GetExtension(target)
                .Equals(".cs", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Restrict(
                await new LooseSourceLoader(_fileSystem).LoadAsync(
                    resolver.ExpandSources(target),
                    options,
                    cancellationToken
                ),
                cancellationToken
            );
        }

        var directory = _fileSystem.Path.GetDirectoryName(target)!;
        var selected = await SdkAvailability.ResolveAsync(directory, cancellationToken);
        var version = selected.Version;
        // MSBuild built for a newer .NET can't load into this runtime.
        if (version.Major > Environment.Version.Major)
            throw new NewerRuntimeRequiredException(selected, directory);
        // MSBuildLocator lists other installed SDKs after the selected one, and it skips a selected SDK that this
        // runtime can't load. Loading any other SDK than the one global.json selects would give wrong compilations.
        var sdk =
            MSBuildLocator
                .QueryVisualStudioInstances(
                    new VisualStudioInstanceQueryOptions
                    {
                        DiscoveryTypes = DiscoveryType.DotNetSdk,
                        WorkingDirectory = directory,
                    }
                )
                .FirstOrDefault(instance => instance.Version == version)
            ?? throw new InvalidOperationException(
                $"MSBuild found no .NET SDK {version} for '{target}', although dotnet selects it there. Run Drill Press with the dotnet installation that holds that SDK."
            );
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterInstance(sdk);
        }

        return Restrict(
            await new SdkSnapshotLoader(_fileSystem).LoadAsync(
                target,
                options,
                sdk.Version.ToString(),
                sdk.MSBuildPath,
                profile,
                cancellationToken
            ),
            cancellationToken
        );
    }

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
