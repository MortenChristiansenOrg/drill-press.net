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
    public virtual async Task<CompilationSnapshot> LoadAsync(
        string target,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken
    ) => (await LoadCompilationsAsync(target, options, cancellationToken)).Snapshot;

    /// <summary>Captures live compilations alongside the transport snapshot for conformance and profiling.</summary>
    public async Task<SnapshotExport> LoadCompilationsAsync(
        string target,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken = default
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
        await SdkAvailability.VerifyAsync(directory, cancellationToken);
        var sdk =
            MSBuildLocator
                .QueryVisualStudioInstances(
                    new VisualStudioInstanceQueryOptions
                    {
                        DiscoveryTypes = DiscoveryType.DotNetSdk,
                        WorkingDirectory = directory,
                    }
                )
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No compatible .NET SDK was found for '{target}'. Install the SDK required by its global.json."
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
                cancellationToken
            ),
            cancellationToken
        );
    }

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
