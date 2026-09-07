using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.BuildHost;

/// <summary>
/// Isolates SDK project evaluation and exports the compiler inputs needed by rule bundles.
/// </summary>
public sealed class BuildHostApplication
{
    private readonly IFileSystem fileSystem;
    private readonly ICompilationSnapshotLoader snapshotLoader;

    /// <summary>Creates the SDK-backed exporter for local C# projects.</summary>
    public BuildHostApplication() : this(new FileSystem())
    {
    }

    private BuildHostApplication(IFileSystem fileSystem) : this(fileSystem, new MsBuildSnapshotLoader(fileSystem))
    {
    }

    internal BuildHostApplication(IFileSystem fileSystem, ICompilationSnapshotLoader snapshotLoader)
    {
        this.fileSystem = fileSystem;
        this.snapshotLoader = snapshotLoader;
    }

    /// <summary>Executes the BuildHost command-line contract.</summary>
    public async Task<BuildHostExitCode> RunAsync(
        string[] args,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default)
    {
        standardError ??= Console.Error;
        if (args is not ["export", var projectPath, var outputPath])
        {
            await standardError.WriteLineAsync(
                "Usage: DrillPress.BuildHost export <project.csproj> <snapshot>");
            return BuildHostExitCode.Failure;
        }

        try
        {
            await ExportAsync(projectPath, outputPath, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullProjectPath = ResolveProjectPath(projectPath);
        var snapshot = await snapshotLoader.LoadAsync(fullProjectPath, cancellationToken);

        await WriteSnapshotAsync(snapshot, outputPath, cancellationToken);
    }

    private string ResolveProjectPath(string projectPath)
    {
        var fullProjectPath = fileSystem.Path.GetFullPath(projectPath);
        if (!fileSystem.File.Exists(fullProjectPath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(fileSystem.Path.GetExtension(fullProjectPath), ".csproj"))
        {
            throw new FileNotFoundException($"C# project '{projectPath}' was not found.", fullProjectPath);
        }

        return fullProjectPath;
    }

    private async Task WriteSnapshotAsync(
        CompilationSnapshot snapshot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var fullOutputPath = fileSystem.Path.GetFullPath(outputPath);
        fileSystem.Directory.CreateDirectory(
            fileSystem.Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("The snapshot output path has no directory."));
        await new CompilationSnapshotFile(fileSystem).WriteAsync(fullOutputPath, snapshot, cancellationToken);
    }
}
