using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Security.Cryptography;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.BuildHost;

internal sealed class GeneratorPlanPublisher(
    IFileSystem fileSystem,
    GeneratorTransport transport,
    GeneratorDescription[] supported,
    string? selectedProject
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly HashSet<GeneratorDescription> _supported = supported.ToHashSet();
    private readonly ConcurrentDictionary<string, Lazy<string>> _fingerprints = new();

    internal async Task PublishAsync(
        Project project,
        ProjectSnapshot snapshot,
        CSharpCompilation compilation,
        ISourceGenerator[] generators,
        AdditionalText[] additionalFiles,
        string[] diagnosticIds,
        CancellationToken cancellationToken
    )
    {
        var descriptions = generators.Select(Describe).ToArray();
        var files = additionalFiles
            .Select(file =>
            {
                var text = file.GetText(cancellationToken);
                var bytes = _fileSystem.File.Exists(file.Path)
                    ? _fileSystem.File.ReadAllBytes(file.Path)
                    : text?.Encoding?.GetBytes(text.ToString());
                return new GeneratorAdditionalInput(
                    file.Path,
                    bytes,
                    text?.Encoding?.WebName,
                    (int)(
                        text?.ChecksumAlgorithm
                        ?? Microsoft.CodeAnalysis.Text.SourceHashAlgorithm.Sha1
                    )
                );
            })
            .ToArray();
        var configurations = new List<GeneratorAnalyzerConfig>();
        foreach (var document in project.AnalyzerConfigDocuments)
            configurations.Add(
                new GeneratorAnalyzerConfig(
                    document.FilePath!,
                    (await document.GetTextAsync(cancellationToken)).ToString()
                )
            );
        snapshot = snapshot with
        {
            IsAnalysisTarget =
                selectedProject is null
                || string.Equals(
                    _fileSystem.Path.GetFullPath(snapshot.ProjectPath),
                    selectedProject,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal
                ),
        };
        await transport.WritePlanAsync(
            new GeneratorProjectPlan(
                snapshot,
                new GeneratorInputs(descriptions, files, configurations.ToArray(), diagnosticIds)
            ),
            cancellationToken
        );

        // Generators share post-initialization sources within one driver, so a project with one
        // generator the bundle lacks runs all of its generators here.
        if (descriptions.All(_supported.Contains))
            return;
        var request = await transport.ReadRequestAsync(snapshot.ContextId, cancellationToken);
        var input = Rebind(compilation, snapshot, request, cancellationToken);
        var driver = GeneratorExecution.Run(
            snapshot.Name,
            CSharpGeneratorDriver.Create(
                generators,
                additionalFiles,
                (CSharpParseOptions)project.ParseOptions!,
                project.AnalyzerOptions.AnalyzerConfigOptionsProvider
            ),
            input,
            cancellationToken
        );
        var results = driver.GetRunResult().Results;
        var sources = results
            .SelectMany(
                (result, index) =>
                    result.GeneratedSources.Select(source => new GeneratedSourceData(
                        index,
                        source.HintName,
                        source.SyntaxTree.FilePath,
                        source.SourceText.ToString(),
                        source.SourceText.Encoding?.WebName,
                        (int)source.SourceText.ChecksumAlgorithm
                    ))
            )
            .ToArray();
        await transport.WriteResponseAsync(
            snapshot.ContextId,
            new GeneratorFallbackResponse(sources),
            cancellationToken
        );
    }

    private GeneratorDescription Describe(ISourceGenerator generator)
    {
        var type = generator.GetGeneratorType();
        var fingerprint = _fingerprints
            .GetOrAdd(
                type.Assembly.Location,
                path => new Lazy<string>(() =>
                    Convert.ToHexString(SHA256.HashData(_fileSystem.File.ReadAllBytes(path)))
                )
            )
            .Value;
        return new GeneratorDescription(type.FullName!, type.Assembly.FullName!, fingerprint);
    }

    private CSharpCompilation Rebind(
        CSharpCompilation compilation,
        ProjectSnapshot project,
        GeneratorFallbackRequest request,
        CancellationToken cancellationToken
    )
    {
        var references = compilation.References.OfType<CompilationReference>().ToArray();
        if (
            request.References.Length != references.Length
            || references.Length != project.CompilationReferences.Length
        )
            throw new InvalidDataException("Generator dependency references do not match.");
        var sources =
            request.SourceContexts.Length == 0
                ? []
                : new SnapshotCompiler(_fileSystem).Reconstruct(
                    CompilationSnapshot.Create(request.SourceContexts),
                    cancellationToken
                );
        for (var index = 0; index < references.Length; index++)
        {
            var edge = project.CompilationReferences[index];
            var supplied = request.References[index];
            if (supplied.ContextId != edge.ContextId)
                throw new InvalidDataException("Generator dependency identities do not match.");
            MetadataReference replacement;
            if (supplied.Image is { } image)
            {
                var bytes = _fileSystem.File.ReadAllBytes(image.Path);
                if (Convert.ToHexString(SHA256.HashData(bytes)) != image.Fingerprint)
                    throw new InvalidDataException("Generator reference assembly changed.");
                replacement = MetadataReference.CreateFromImage(
                    bytes,
                    references[index].Properties,
                    filePath: image.Path
                );
            }
            else
                replacement = sources
                    .Single(source => source.Snapshot.ContextId == edge.ContextId)
                    .Compilation.ToMetadataReference(
                        edge.Aliases.ToImmutableArray(),
                        edge.EmbedInteropTypes
                    );
            compilation = compilation.ReplaceReference(references[index], replacement);
        }
        return compilation;
    }
}
