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
    private readonly SnapshotInputCache _cache;
    private readonly ProjectGenerationCache _generations;

    /// <summary>Creates a loader for local SDK and loose-source targets.</summary>
    public MsBuildSnapshotLoader()
        : this(new FileSystem()) { }

    internal MsBuildSnapshotLoader(IFileSystem fileSystem)
        : this(fileSystem, new FileIdentityProbe()) { }

    internal MsBuildSnapshotLoader(IFileSystem fileSystem, FileIdentityProbe probe)
    {
        _fileSystem = fileSystem;
        _sourcePolicy = new SourceFilePolicy(fileSystem, probe);
        _cache = new SnapshotInputCache(fileSystem);
        _generations = new ProjectGenerationCache(fileSystem);
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
        var reuseInputs =
            options.ReuseInputs
            && target.IndexOfAny(['*', '?']) < 0
            && !_fileSystem
                .Path.GetExtension(target)
                .Equals(".cs", StringComparison.OrdinalIgnoreCase);
        var cacheKey =
            target
            + "\n"
            + options.IncludeReferencedProjects
            + "\n"
            + options.ValidateCompilation
            + string.Join(
                "\n",
                options
                    .Properties.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Key + "=" + pair.Value)
            );
        if (reuseInputs && _cache.Find(cacheKey, cancellationToken) is { } cached)
            return Restrict(cached, cancellationToken);
        var export = await CaptureAsync(target, options, profile, cancellationToken);
        if (reuseInputs)
            _cache.Store(cacheKey, export, cancellationToken);
        return Restrict(export, cancellationToken);
    }

    // The external compiler loader consumes real paths; cache policy is independent of that consumer.
    internal virtual async Task<SnapshotExport> CaptureAsync(
        string target,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        if (
            target.IndexOfAny(['*', '?']) >= 0
            || _fileSystem
                .Path.GetExtension(target)
                .Equals(".cs", StringComparison.OrdinalIgnoreCase)
        )
        {
            return await new LooseSourceLoader(_fileSystem).LoadAsync(
                new TargetResolver(_fileSystem).ExpandSources(target),
                options,
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

        return await new SdkSnapshotLoader(
            _fileSystem,
            options.ReuseInputs ? _generations : null
        ).LoadAsync(
            target,
            options,
            sdk.Version.ToString(),
            sdk.MSBuildPath,
            profile,
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
                    (context, project) => context with { Snapshot = project }
                )
                .ToArray()
        );
    }
}
