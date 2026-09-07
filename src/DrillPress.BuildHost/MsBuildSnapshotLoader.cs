using System.Collections.Concurrent;
using System.IO.Abstractions;
using DrillPress.Manifest;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.MSBuild;

namespace DrillPress.BuildHost;

/// <summary>Loads compiler inputs through the installed SDK; projects must exist on the OS filesystem.</summary>
/// <param name="fileSystem">The real filesystem shared with MSBuild and source generators.</param>
public sealed class MsBuildSnapshotLoader(IFileSystem fileSystem) : ICompilationSnapshotLoader
{
    /// <inheritdoc />
    public async Task<CompilationSnapshot> LoadAsync(
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

    private ProjectSnapshot CreateProjectSnapshot(
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

    private DocumentSnapshot[] CreateDocumentSnapshots(
        Project project,
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var ordinaryDocumentPaths = project.Documents
            .Select(document => document.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => fileSystem.Path.GetFullPath(path!))
            .ToHashSet(PathComparer);
        var documents = compilation.SyntaxTrees
            .Select((tree, index) =>
            {
                var path = string.IsNullOrWhiteSpace(tree.FilePath)
                    ? $"drillpress-generated://{project.Name}/{index:D6}.g.cs"
                    : fileSystem.Path.GetFullPath(tree.FilePath);
                return new DocumentSnapshot(
                    path,
                    tree.GetText(cancellationToken).ToString(),
                    !ordinaryDocumentPaths.Contains(path));
            })
            .OrderBy(document => document.Path)
            .ToArray();

        return documents;
    }

    private string[] CreateMetadataReferencePaths(CSharpCompilation compilation)
    {
        return compilation.References
            .OfType<PortableExecutableReference>()
            .Select(reference => reference.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && fileSystem.File.Exists(path))
            .Select(path => fileSystem.Path.GetFullPath(path!))
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

    private static IEqualityComparer<string> PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : EqualityComparer<string>.Default;
}
