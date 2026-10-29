using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Text;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class GeneratorProjectProcessor(
    IFileSystem fileSystem,
    SourceGeneratorCatalog catalog,
    GeneratorTransport transport,
    SourceFilePolicy sourceFiles,
    AnalysisOptions options
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CompilerCapture _capture = new(fileSystem);

    internal async Task<GeneratedAnalysisContext> GenerateAsync(
        GeneratorProjectPlan plan,
        CompilationContext context,
        GeneratorDependency[] dependencies,
        CancellationToken cancellationToken
    )
    {
        var completed = await Task.WhenAll(
            dependencies.Select(dependency => dependency.Generation)
        );
        var live = AttachSources(context.Compilation, dependencies, completed);
        var configuration = new GeneratorOptions(plan.Inputs.Configurations, _fileSystem);
        live = live.WithOptions(
            live.Options.WithSyntaxTreeOptionsProvider(
                new GeneratorTreeOptions(configuration, live.Options.SyntaxTreeOptionsProvider)
            )
        );
        var input = CreateGeneratorInput(live, completed, out var replaced);

        var selections = plan
            .Inputs.Generators.Select(
                (description, index) => (Generator: catalog.Find(description), Index: index)
            )
            .ToArray();
        Task<GeneratorFallbackResponse>? fallback = null;
        if (selections.Any(selection => selection.Generator is null))
        {
            await transport.WriteRequestAsync(
                plan.Project.ContextId,
                CreateFallbackRequest(dependencies, completed),
                cancellationToken
            );
            fallback = transport.ReadResponseAsync(plan.Project.ContextId, cancellationToken);
        }

        var generated = await GenerateTreesAsync(
            plan,
            input,
            configuration,
            selections,
            fallback,
            cancellationToken
        );
        var output = input.AddSyntaxTrees(generated);
        foreach (var (source, image) in replaced)
            output = output.ReplaceReference(image, source);
        var captured = _capture.Capture(
            plan.Project.Name,
            plan.Project.ProjectPath,
            plan.Project.ContextId,
            output,
            generated.ToHashSet(),
            plan.Inputs.DiagnosticIds,
            cancellationToken,
            baseline: plan.Project
        );
        var snapshot = plan.Project with
        {
            Documents = captured.Documents,
            ExternalReferences = captured.ExternalReferences,
            CompilerOptions = captured.CompilerOptions,
        };
        snapshot = sourceFiles
            .Restrict(CompilationSnapshot.Create([snapshot]), cancellationToken)
            .Projects.Single();
        var analysis = AttachSources(output, dependencies, completed, analysis: true);
        analysis = analysis.WithOptions(
            analysis.Options.WithSyntaxTreeOptionsProvider(
                new SnapshotTreeOptions(analysis.SyntaxTrees.Zip(snapshot.Documents).ToDictionary())
            )
        );
        var preparation = new SemanticPreparation(
            new CompilationContext(snapshot, analysis),
            _fileSystem,
            options.EnableOptimizations,
            cancellationToken
        );
        return new GeneratedAnalysisContext(
            snapshot,
            output,
            preparation,
            new GeneratorReferenceAssembly(
                output,
                snapshot.ContextId,
                transport,
                cancellationToken
            ),
            completed
                .SelectMany(project => project.SourceContexts)
                .Append(snapshot)
                .DistinctBy(project => project.ContextId)
                .ToArray()
        );
    }

    private static CSharpCompilation CreateGeneratorInput(
        CSharpCompilation live,
        GeneratedAnalysisContext[] completed,
        out List<(MetadataReference Source, MetadataReference Image)> replaced
    )
    {
        var input = live;
        replaced = [];
        var sourceReferences = live.References.OfType<CompilationReference>().ToArray();
        for (var index = 0; index < completed.Length; index++)
        {
            if (
                completed[index]
                    .Assembly.Reference?.WithProperties(sourceReferences[index].Properties) is
                { } image
            )
            {
                input = input.ReplaceReference(sourceReferences[index], image);
                replaced.Add((sourceReferences[index], image));
            }
        }

        return input;
    }

    private static GeneratorFallbackRequest CreateFallbackRequest(
        GeneratorDependency[] dependencies,
        GeneratedAnalysisContext[] completed
    )
    {
        var references = dependencies
            .Select(
                (dependency, index) =>
                {
                    var image = completed[index].Assembly.Image;
                    return new GeneratorReferenceInput(
                        dependency.Reference.ContextId,
                        image is null
                            ? null
                            : image with
                            {
                                Aliases = dependency.Reference.Aliases,
                                EmbedInteropTypes = dependency.Reference.EmbedInteropTypes,
                            }
                    );
                }
            )
            .ToArray();
        var sources = references.Any(reference => reference.Image is null)
            ? completed
                .SelectMany(project => project.SourceContexts)
                .DistinctBy(project => project.ContextId)
                .ToArray()
            : [];
        return new GeneratorFallbackRequest(references, sources);
    }

    private async Task<SyntaxTree[]> GenerateTreesAsync(
        GeneratorProjectPlan plan,
        CSharpCompilation input,
        GeneratorOptions configuration,
        (ISourceGenerator? Generator, int Index)[] selections,
        Task<GeneratorFallbackResponse>? fallback,
        CancellationToken cancellationToken
    )
    {
        using var generating = options.Profile.Measure("generation." + plan.Project.Name);
        var parse = (CSharpParseOptions)(
            input.SyntaxTrees.FirstOrDefault()?.Options
            ?? new CSharpParseOptions((LanguageVersion)plan.Project.LanguageVersion)
        );
        var known = selections.Where(selection => selection.Generator is not null).ToArray();
        var trees = new Dictionary<int, SyntaxTree[]>();
        if (known.Length > 0)
        {
            var driver = GeneratorExecution.Run(
                plan.Project.Name,
                CSharpGeneratorDriver.Create(
                    known.Select(selection => selection.Generator!),
                    plan.Inputs.AdditionalFiles.Select(file => new GeneratorAdditionalText(file)),
                    parse,
                    configuration
                ),
                input,
                cancellationToken
            );
            var results = driver.GetRunResult().Results;
            for (var index = 0; index < known.Length; index++)
                trees.Add(
                    known[index].Index,
                    results[index].GeneratedSources.Select(source => source.SyntaxTree).ToArray()
                );
        }
        if (fallback is not null)
        {
            foreach (var group in (await fallback).Sources.GroupBy(source => source.GeneratorIndex))
                trees.Add(
                    group.Key,
                    group
                        .Select(source =>
                            CSharpSyntaxTree.ParseText(
                                SourceText.From(
                                    source.Text,
                                    source.EncodingName is null
                                        ? null
                                        : Encoding.GetEncoding(source.EncodingName),
                                    (SourceHashAlgorithm)source.ChecksumAlgorithm
                                ),
                                parse,
                                source.Path,
                                cancellationToken
                            )
                        )
                        .ToArray()
                );
        }
        return Enumerable
            .Range(0, selections.Length)
            .SelectMany(index => trees.GetValueOrDefault(index) ?? [])
            .ToArray();
    }

    private static CSharpCompilation AttachSources(
        CSharpCompilation compilation,
        GeneratorDependency[] dependencies,
        GeneratedAnalysisContext[] completed,
        bool analysis = false
    )
    {
        var original = compilation.References.OfType<CompilationReference>().ToArray();
        for (var index = 0; index < completed.Length; index++)
        {
            var edge = dependencies[index].Reference;
            var target = analysis
                ? completed[index].Preparation.Project.Compilation
                : completed[index].Live;
            compilation = compilation.ReplaceReference(
                original[index],
                target.ToMetadataReference(edge.Aliases.ToImmutableArray(), edge.EmbedInteropTypes)
            );
        }
        return compilation;
    }
}
