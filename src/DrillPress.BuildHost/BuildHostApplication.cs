using System.Collections.Concurrent;
using DrillPress.Manifest;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.MSBuild;

namespace DrillPress.BuildHost;

/// <summary>
/// Isolates SDK project evaluation and exports the compiler inputs needed by rule bundles.
/// </summary>
public static class BuildHostApplication
{
    /// <summary>Executes the BuildHost command-line contract.</summary>
    public static async Task<BuildHostExitCode> RunAsync(
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
    public static async Task ExportAsync(
        string projectPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var fullProjectPath = ResolveProjectPath(projectPath);
        var snapshot = await LoadSnapshotAsync(fullProjectPath, cancellationToken);

        await WriteSnapshotAsync(snapshot, outputPath, cancellationToken);
    }

    private static string ResolveProjectPath(string projectPath)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(fullProjectPath), ".csproj"))
        {
            throw new FileNotFoundException($"C# project '{projectPath}' was not found.", fullProjectPath);
        }

        return fullProjectPath;
    }

    private static async Task<CompilationSnapshot> LoadSnapshotAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        using var workspace = MSBuildWorkspace.Create();
        var workspaceFailures = new ConcurrentQueue<string>();
        workspace.RegisterWorkspaceFailedHandler(eventArgs =>
        {
            if (eventArgs.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                workspaceFailures.Enqueue(eventArgs.Diagnostic.Message);
            }
        });
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        _ = await project.GetSourceGeneratedDocumentsAsync(cancellationToken);
        var compilation = await GetCompilationAsync(project, projectPath, cancellationToken);

        if (workspaceFailures.Count > 0)
        {
            throw new InvalidOperationException(
                $"MSBuild could not load '{projectPath}': {string.Join("; ", workspaceFailures)}");
        }

        if (project.ParseOptions is not CSharpParseOptions parseOptions)
        {
            throw new InvalidOperationException("The project has no C# parse options.");
        }

        return CompilationSnapshot.Create(CreateProjectSnapshot(
            project,
            compilation,
            parseOptions,
            projectPath,
            cancellationToken));
    }

    private static async Task<CSharpCompilation> GetCompilationAsync(
        Project project,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        return compilation is CSharpCompilation csharpCompilation
            ? csharpCompilation
            : throw new InvalidOperationException(
                $"Could not create a C# compilation for '{projectPath}'.");
    }

    private static ProjectSnapshot CreateProjectSnapshot(
        Project project,
        CSharpCompilation compilation,
        CSharpParseOptions parseOptions,
        string projectPath,
        CancellationToken cancellationToken)
    {
        return new ProjectSnapshot(
            project.Name,
            project.AssemblyName ?? project.Name,
            projectPath,
            (int)parseOptions.LanguageVersion,
            (int)compilation.Options.OutputKind,
            (int)compilation.Options.NullableContextOptions,
            parseOptions.PreprocessorSymbolNames.ToArray(),
            CreateDocumentSnapshots(project, compilation, cancellationToken),
            CreateMetadataReferencePaths(compilation))
        {
            ProjectReferences = CreateProjectReferenceImages(compilation, cancellationToken),
        };
    }

    private static DocumentSnapshot[] CreateDocumentSnapshots(
        Project project,
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var ordinaryDocumentPaths = project.Documents
            .Select(document => document.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(PathComparer);
        var documents = compilation.SyntaxTrees
            .Select((tree, index) =>
            {
                var path = string.IsNullOrWhiteSpace(tree.FilePath)
                    ? $"drillpress-generated://{project.Name}/{index:D6}.g.cs"
                    : Path.GetFullPath(tree.FilePath);
                return new DocumentSnapshot(
                    path,
                    tree.GetText(cancellationToken).ToString(),
                    !ordinaryDocumentPaths.Contains(path));
            })
            .OrderBy(document => document.Path)
            .ToArray();

        return documents;
    }

    private static string[] CreateMetadataReferencePaths(CSharpCompilation compilation)
    {
        return compilation.References
            .OfType<PortableExecutableReference>()
            .Select(reference => reference.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .OrderBy(path => path)
            .ToArray();
    }

    private static MetadataImageSnapshot[] CreateProjectReferenceImages(
        CSharpCompilation compilation,
        CancellationToken cancellationToken) =>
        compilation.References.OfType<CompilationReference>()
            .Select(reference => CreateMetadataImage(reference, cancellationToken))
            .ToArray();

    private static MetadataImageSnapshot CreateMetadataImage(
        CompilationReference reference,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var result = reference.Compilation.Emit(
            stream,
            options: new EmitOptions(metadataOnly: true, includePrivateMembers: true),
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException(
                $"Could not export project reference '{reference.Compilation.AssemblyName}': {string.Join("; ", errors)}");
        }

        return new MetadataImageSnapshot(
            stream.ToArray(), reference.Properties.Aliases.ToArray(), reference.Properties.EmbedInteropTypes);
    }

    private static async Task WriteSnapshotAsync(
        CompilationSnapshot snapshot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("The snapshot output path has no directory."));
        await snapshot.WriteAsync(fullOutputPath, cancellationToken);
    }

    private static IEqualityComparer<string> PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : EqualityComparer<string>.Default;
}
