using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.BuildHost;

/// <summary>
/// Isolates SDK project evaluation and exports the compiler inputs needed by rule bundles.
/// </summary>
public sealed class BuildHostApplication
{
    private readonly IFileSystem _fileSystem;
    private readonly MsBuildSnapshotLoader _snapshotLoader;
    private readonly ProcessProfileProbe _profileProbe;
    private bool _reuseInputs;

    /// <summary>Creates the SDK-backed exporter for local C# projects.</summary>
    public BuildHostApplication()
        : this(new FileSystem()) { }

    private BuildHostApplication(IFileSystem fileSystem)
        : this(fileSystem, new MsBuildSnapshotLoader(fileSystem)) { }

    internal BuildHostApplication(IFileSystem fileSystem, MsBuildSnapshotLoader snapshotLoader)
        : this(fileSystem, snapshotLoader, new ProcessProfileProbe()) { }

    internal BuildHostApplication(
        IFileSystem fileSystem,
        MsBuildSnapshotLoader snapshotLoader,
        ProcessProfileProbe profileProbe
    )
    {
        _fileSystem = fileSystem;
        _snapshotLoader = snapshotLoader;
        _profileProbe = profileProbe;
    }

    /// <summary>Executes the BuildHost command-line contract.</summary>
    public async Task<BuildHostExitCode> RunAsync(
        string[] args,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default
    )
    {
        standardError ??= Console.Error;
        if (args is [CommandWorker.Command, var name])
        {
            _reuseInputs = true;
            await new CommandWorker().RunAsync(
                name,
                async (arguments, token) =>
                {
                    using var error = new BoundedStringWriter(8 * 1024 * 1024, "standard error");
                    var code = await RunAsync(arguments, error, token);
                    return new WorkerResponse((int)code, "", error.ToString());
                },
                cancellationToken
            );
            return BuildHostExitCode.Success;
        }
        if (args.Length < 3 || args[0] != "export")
        {
            await standardError.WriteLineAsync(
                "Usage: DrillPress.BuildHost export <target> <snapshot> [--property Name=Value] [--validate-compilation] [--include-referenced-projects] [--profile]"
            );
            return BuildHostExitCode.Failure;
        }

        var profile = new PipelineProfile(
            args.Skip(3).Contains("--profile"),
            standardError,
            "build-host",
            _profileProbe
        );
        using var total = profile.Measure("total");
        try
        {
            var options = ParseOptions(args[3..]) with { ReuseInputs = _reuseInputs };
            await ExportAsync(args[1], args[2], options, profile, cancellationToken);
            return BuildHostExitCode.Success;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await standardError.WriteLineAsync($"DrillPress.BuildHost: {exception.Message}");
            return BuildHostExitCode.Failure;
        }
    }

    /// <summary>
    /// Loads <paramref name="projectPath"/> with the registered .NET SDK and writes its
    /// effective C# compilation to <paramref name="outputPath"/>.
    /// </summary>
    public async Task ExportAsync(
        string projectPath,
        string outputPath,
        CancellationToken cancellationToken = default
    )
    {
        await ExportAsync(projectPath, outputPath, new SnapshotLoadOptions(), cancellationToken);
    }

    /// <summary>Exports a resolved solution, project, directory, or loose-source target with explicit evaluation options.</summary>
    public async Task ExportAsync(
        string target,
        string outputPath,
        SnapshotLoadOptions options,
        CancellationToken cancellationToken = default
    )
    {
        await ExportAsync(
            target,
            outputPath,
            options,
            new PipelineProfile(false, TextWriter.Null, "build-host", _profileProbe),
            cancellationToken
        );
    }

    private async Task ExportAsync(
        string target,
        string outputPath,
        SnapshotLoadOptions options,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        CompilationSnapshot snapshot;
        using (profile.Measure("loading"))
        {
            var resolved = new TargetResolver(_fileSystem).Resolve(target);
            snapshot = await _snapshotLoader.LoadAsync(
                resolved,
                options,
                profile,
                cancellationToken
            );
        }

        using (profile.Measure("snapshot.serialization"))
        {
            await WriteSnapshotAsync(snapshot, outputPath, cancellationToken);
        }

        if (profile.Enabled)
        {
            try
            {
                profile.Count(
                    "snapshot.bytes",
                    _fileSystem.FileInfo.New(_fileSystem.Path.GetFullPath(outputPath)).Length
                );
                profile.Count("contexts", snapshot.Projects.Length);
            }
            catch (Exception exception)
            {
                profile.Fail(exception);
            }
        }
    }

    private static SnapshotLoadOptions ParseOptions(string[] args)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var validate = false;
        string? generationDirectory = null;
        string? generatorCatalog = null;
        var includeReferencedProjects = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--generator-catalog" && index + 1 < args.Length)
            {
                generatorCatalog = args[++index];
                continue;
            }
            if (args[index] == "--generation-plan" && index + 1 < args.Length)
            {
                generationDirectory = args[++index];
                continue;
            }
            if (args[index] == "--validate-compilation")
            {
                validate = true;
            }
            else if (args[index] == "--include-referenced-projects")
            {
                includeReferencedProjects = true;
            }
            else if (args[index] == "--profile")
            {
                continue;
            }
            else if (
                args[index] == "--property"
                && ++index < args.Length
                && args[index].IndexOf('=') is > 0 and var separator
            )
            {
                properties[args[index][..separator]] = args[index][(separator + 1)..];
            }
            else
            {
                throw new ArgumentException(
                    "Expected --property Name=Value, --validate-compilation, --include-referenced-projects, or --profile."
                );
            }
        }

        return new SnapshotLoadOptions
        {
            Properties = properties,
            ValidateCompilation = validate,
            GenerationPlanDirectory = generationDirectory,
            GeneratorCatalogPath = generatorCatalog,
            IncludeReferencedProjects = includeReferencedProjects,
        };
    }

    private async Task WriteSnapshotAsync(
        CompilationSnapshot snapshot,
        string outputPath,
        CancellationToken cancellationToken
    )
    {
        var fullOutputPath = _fileSystem.Path.GetFullPath(outputPath);
        _fileSystem.Directory.CreateDirectory(
            _fileSystem.Path.GetDirectoryName(fullOutputPath)
                ?? throw new InvalidOperationException("The snapshot output path has no directory.")
        );
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            fullOutputPath,
            snapshot,
            cancellationToken
        );
    }
}
