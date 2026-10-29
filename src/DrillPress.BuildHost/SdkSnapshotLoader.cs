using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.Build.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.MSBuild;
using Project = Microsoft.CodeAnalysis.Project;

namespace DrillPress.BuildHost;

internal sealed class SdkSnapshotLoader(
    IFileSystem fileSystem,
    ProjectGenerationCache? generationCache = null
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ConcurrentDictionary<string, byte> _inputs = new();
    private readonly ConcurrentDictionary<string, byte> _inputDirectories = new();
    private readonly ProjectGenerationCache? _generations = generationCache;
    private GeneratorPlanPublisher? _publisher;
    private readonly GeneratorBuildCollector _generatorBuild = new(fileSystem);
    private IReadOnlyDictionary<ProjectId, string> _contextIds =
        new Dictionary<ProjectId, string>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<SnapshotExport> LoadAsync(
        string target,
        SnapshotLoadOptions options,
        string sdkVersion,
        string sdkPath,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        _inputs.TryAdd(target, 0);
        for (
            var directory = _fileSystem.Path.GetDirectoryName(target);
            directory is not null;
            directory = _fileSystem.Path.GetDirectoryName(directory)
        )
        {
            foreach (
                var name in new[]
                {
                    "global.json",
                    "Directory.Build.props",
                    "Directory.Build.targets",
                    "Directory.Packages.props",
                    "NuGet.Config",
                    ".editorconfig",
                }
            )
                _inputs.TryAdd(_fileSystem.Path.Combine(directory, name), 0);
        }
        var loadedDirectory = _fileSystem.Path.GetDirectoryName(
            typeof(ProjectCollection).Assembly.Location
        )!;
        if (
            !string.Equals(
                _fileSystem.Path.GetFullPath(loadedDirectory),
                _fileSystem.Path.GetFullPath(sdkPath),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "This process already loaded a different MSBuild SDK. Export this target in a new BuildHost process."
            );
        }

        if (options.GenerationPlanDirectory is { } generationDirectory)
        {
            var transport = new GeneratorTransport(_fileSystem, generationDirectory);
            _publisher = new GeneratorPlanPublisher(
                _fileSystem,
                transport,
                await transport.ReadCapabilitiesAsync(cancellationToken),
                !options.IncludeReferencedProjects
                && _fileSystem
                    .Path.GetExtension(target)
                    .Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                    ? _fileSystem.Path.GetFullPath(target)
                    : null
            );
        }

        using var workspace = MSBuildWorkspace.Create(options.Properties);
        using var direct = new DirectWorkspaceLoader(_fileSystem);
        workspace.LoadMetadataForReferencedProjects = false;
        var failures = new ConcurrentQueue<string>();
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                failures.Enqueue(args.Diagnostic.Message);
            }
        });
        await using var streamingEvaluator = _publisher is null
            ? null
            : new ProjectEvaluator(_fileSystem, options.Properties);
        var streaming = streamingEvaluator is null
            ? null
            : new ProjectCaptureState(
                streamingEvaluator,
                new CompilerCapture(_fileSystem),
                new(),
                [],
                [],
                []
            );
        if (streaming is not null)
            _contextIds = streaming.Identities;
        Solution original;
        using (profile.Measure("loading.workspace"))
        {
            var directSolution = direct.Load(
                workspace,
                target,
                options.Properties,
                cancellationToken,
                streaming is null
                    ? null
                    : project =>
                        ScheduleProject(
                            project,
                            streaming,
                            options,
                            sdkVersion,
                            failures,
                            profile,
                            cancellationToken
                        )
            );
            if (directSolution is not null)
                original = directSolution;
            else if (
                _fileSystem
                    .Path.GetExtension(target)
                    .Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            )
            {
                await workspace.OpenProjectAsync(target, cancellationToken: cancellationToken);
                original = workspace.CurrentSolution;
            }
            else
            {
                await workspace.OpenSolutionAsync(target, cancellationToken: cancellationToken);
                original = workspace.CurrentSolution;
            }
        }

        ThrowLoadFailures(failures);
        var solution = original;
        foreach (var project in original.Projects)
        {
            solution = solution.WithProjectAnalyzerReferences(project.Id, []);
        }

        CompilationContext[] captured;
        using (profile.Measure("loading.contexts"))
        {
            captured = streaming is { Contexts.Count: > 0 }
                ? await FinishStreamingAsync(streaming, cancellationToken)
                : await CaptureProjectsAsync(
                    original,
                    solution,
                    options,
                    sdkVersion,
                    failures,
                    profile,
                    cancellationToken
                );
        }

        if (options.GeneratorCatalogPath is { } catalogPath)
            await _generatorBuild.WriteAsync(catalogPath, cancellationToken);

        var projectOnly =
            !options.IncludeReferencedProjects
            && _fileSystem
                .Path.GetExtension(target)
                .Equals(".csproj", StringComparison.OrdinalIgnoreCase);
        var contexts = captured
            .Select(context =>
                context with
                {
                    Snapshot = context.Snapshot with
                    {
                        IsAnalysisTarget =
                            !projectOnly
                            || string.Equals(
                                _fileSystem.Path.GetFullPath(context.Snapshot.ProjectPath),
                                _fileSystem.Path.GetFullPath(target),
                                OperatingSystem.IsWindows()
                                    ? StringComparison.OrdinalIgnoreCase
                                    : StringComparison.Ordinal
                            ),
                    },
                }
            )
            .OrderBy(context => context.Snapshot.ProjectPath, StringComparer.Ordinal)
            .ThenBy(context => context.Snapshot.TargetFramework, StringComparer.Ordinal)
            .ToArray();
        var envelope = CompilationSnapshot.Create(
            contexts.Select(context => context.Snapshot).ToArray()
        );
        SnapshotValidation.Validate(envelope);
        return new SnapshotExport(envelope, contexts)
        {
            InputPaths = _inputs.Keys.Order(StringComparer.Ordinal).ToArray(),
            InputDirectories = _inputDirectories.Keys.Order(StringComparer.Ordinal).ToArray(),
        };
    }

    private void ScheduleProject(
        Project liveProject,
        ProjectCaptureState state,
        SnapshotLoadOptions options,
        string sdkVersion,
        ConcurrentQueue<string> failures,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        state.Identities.TryAdd(liveProject.Id, liveProject.Id.Id.ToString("N"));
        var project = liveProject.WithAnalyzerReferences([]);
        var evaluation = state.Evaluator.Start(liveProject, cancellationToken);
        var dependencies = project
            .ProjectReferences.Select(reference => reference.ProjectId)
            .Distinct()
            .ToDictionary(id => id, id => (Task<GeneratedProject>?)state.Generations[id]);
        var generation = Task.Run(
            () =>
                GenerateAsync(
                    project,
                    liveProject,
                    evaluation,
                    dependencies,
                    state.Capture,
                    options,
                    sdkVersion,
                    profile,
                    cancellationToken
                ),
            CancellationToken.None
        );
        state.Generations.Add(project.Id, generation);
        state.Order.Add(project);
        state.Contexts.Add(
            project.Id,
            CaptureProjectAsync(
                project,
                liveProject,
                generation,
                state.Capture,
                options,
                sdkVersion,
                failures,
                profile,
                cancellationToken
            )
        );
    }

    private static async Task<CompilationContext[]> FinishStreamingAsync(
        ProjectCaptureState state,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await Task.WhenAll(state.Contexts.Values);
        }
        catch
        {
            ThrowFirstFailure(state.Order.ToArray(), state.Contexts, cancellationToken);
            throw;
        }
        return state.Order.Select(project => state.Contexts[project.Id].Result).ToArray();
    }

    // Each context generates as soon as the contexts it references have generated, so independent
    // contexts evaluate, compile and run generators in parallel. Dependents do not wait for a
    // reference's validation or capture.
    private async Task<CompilationContext[]> CaptureProjectsAsync(
        Solution original,
        Solution solution,
        SnapshotLoadOptions options,
        string sdkVersion,
        ConcurrentQueue<string> failures,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        var order = solution
            .GetProjectDependencyGraph()
            .GetTopologicallySortedProjects(cancellationToken)
            .Select(id => solution.GetProject(id)!)
            .ToArray();
        _contextIds =
            _generations?.Begin(solution, options)
            ?? order.ToDictionary(project => project.Id, project => project.Id.Id.ToString("N"));
        await using var evaluator = new ProjectEvaluator(_fileSystem, options.Properties);
        var evaluations = evaluator.Start(order, cancellationToken);
        var capture = new CompilerCapture(_fileSystem);
        var generations = new Dictionary<ProjectId, Task<GeneratedProject>>();
        var contexts = new Dictionary<ProjectId, Task<CompilationContext>>();
        foreach (var project in order)
        {
            var dependencies = project
                .ProjectReferences.Select(reference => reference.ProjectId)
                .Distinct()
                .ToDictionary(id => id, id => generations.GetValueOrDefault(id));
            var liveProject = original.GetProject(project.Id)!;
            var evaluation = evaluations[project.Id];
            var generation = Task.Run(
                () =>
                    GenerateAsync(
                        project,
                        liveProject,
                        evaluation,
                        dependencies,
                        capture,
                        options,
                        sdkVersion,
                        profile,
                        cancellationToken
                    ),
                CancellationToken.None
            );
            generations.Add(project.Id, generation);
            contexts.Add(
                project.Id,
                CaptureProjectAsync(
                    project,
                    liveProject,
                    generation,
                    capture,
                    options,
                    sdkVersion,
                    failures,
                    profile,
                    cancellationToken
                )
            );
        }

        try
        {
            await Task.WhenAll(contexts.Values);
        }
        catch
        {
            ThrowFirstFailure(order, contexts, cancellationToken);
            throw;
        }

        return order.Select(project => contexts[project.Id].Result).ToArray();
    }

    // Reports the failure of the first context in snapshot order whose references were captured;
    // a context whose reference failed only repeats that failure. The choice is independent of timing.
    private static void ThrowFirstFailure(
        Project[] order,
        Dictionary<ProjectId, Task<CompilationContext>> contexts,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failed = order
            .Where(project => !contexts[project.Id].IsCompletedSuccessfully)
            .Select(project => project.Id)
            .ToHashSet();
        var first = order
            .Where(project =>
                failed.Contains(project.Id)
                && !project.ProjectReferences.Any(reference => failed.Contains(reference.ProjectId))
            )
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .First();
        var exception = contexts[first.Id].Exception?.InnerException;
        if (exception is not null)
        {
            ExceptionDispatchInfo.Throw(exception);
        }
    }

    private async Task<GeneratedProject> GenerateAsync(
        Project project,
        Project liveProject,
        Task<EvaluatedMetadata> evaluation,
        Dictionary<ProjectId, Task<GeneratedProject>?> dependencies,
        CompilerCapture capture,
        SnapshotLoadOptions options,
        string sdkVersion,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        if (project.Language != LanguageNames.CSharp)
        {
            throw new InvalidOperationException(
                $"Project '{project.Name}' is not C#; its source context cannot be captured."
            );
        }

        // Parsing overlaps evaluation, but an evaluation failure is still reported first.
        var compiling = project.WithProjectReferences([]).GetCompilationAsync(cancellationToken);
        await Task.WhenAny(Task.WhenAll(evaluation, compiling));
        var metadata = await evaluation;
        if (!_fileSystem.File.Exists(metadata.AssetsPath))
        {
            throw new FileNotFoundException(
                $"Restore assets are missing for '{project.FilePath}'. Run dotnet restore for the target first.",
                metadata.AssetsPath
            );
        }

        var analyzers = new TargetCompiler(_fileSystem).Select(
            project.Name,
            liveProject.AnalyzerReferences,
            metadata.CompilerAssemblyPath
        );
        var sourceGenerators = analyzers.Load(reference =>
            reference.GetGenerators(LanguageNames.CSharp)
        );
        if (options.GeneratorCatalogPath is not null)
            _generatorBuild.Add(sourceGenerators, liveProject.AnalyzerReferences);
        var driver = CreateGeneratorDriver(liveProject, sourceGenerators);

        var compilation =
            await compiling as CSharpCompilation
            ?? throw new InvalidOperationException(
                $"Could not load C# compilation '{project.Name}'."
            );
        var edges = new List<CompilationReferenceSnapshot>();
        var dependenciesReady = new List<(ProjectReference Reference, GeneratedProject Project)>();
        foreach (var reference in project.ProjectReferences)
        {
            if (dependencies[reference.ProjectId] is not { } pending)
                throw new InvalidDataException($"Incomplete project graph for '{project.Name}'.");
            var dependency = await pending;
            dependenciesReady.Add((reference, dependency));
            edges.Add(
                new CompilationReferenceSnapshot(
                    dependency.ContextId,
                    reference.Aliases.ToArray(),
                    reference.EmbedInteropTypes
                )
            );
        }
        var diagnosticIds = await GetDiagnosticIdsAsync(liveProject, cancellationToken);
        var signature = "";
        if (_generations is not null)
        {
            var input = WithMetadata(
                capture.Capture(
                    project.Name,
                    project.FilePath!,
                    _contextIds[project.Id],
                    compilation,
                    [],
                    diagnosticIds,
                    cancellationToken
                ),
                metadata,
                edges.ToArray(),
                options,
                sdkVersion
            );
            signature = _generations.Fingerprint(
                input,
                metadata,
                dependenciesReady.Select(item => item.Project.Signature),
                cancellationToken
            );
            if (_generations.Find(_contextIds[project.Id], signature) is { } cached)
            {
                profile.Count("loading.reused." + project.Name, 1);
                return cached;
            }
        }
        var sources = new List<SourceReference>();
        foreach (var (reference, dependency) in dependenciesReady)
        {
            var newReference = dependency.Compilation.ToMetadataReference(
                reference.Aliases,
                reference.EmbedInteropTypes
            );
            compilation = compilation.AddReferences(newReference);
            sources.Add(new SourceReference(newReference, dependency.Assembly));
        }

        if (_publisher is not null)
        {
            var input = WithMetadata(
                capture.Capture(
                    project.Name,
                    project.FilePath!,
                    _contextIds[project.Id],
                    compilation,
                    [],
                    diagnosticIds,
                    cancellationToken
                ),
                metadata,
                edges.ToArray(),
                options,
                sdkVersion
            );
            await _publisher.PublishAsync(
                liveProject,
                input,
                compilation,
                sourceGenerators,
                AdditionalFilesInBuildOrder(liveProject).ToArray(),
                diagnosticIds,
                cancellationToken
            );
            return new GeneratedProject(
                _contextIds[project.Id],
                signature,
                diagnosticIds,
                metadata,
                compilation,
                [],
                edges.ToArray(),
                analyzers,
                new Lazy<MetadataReference?>(() =>
                    EmitReferenceAssembly(compilation, cancellationToken)
                )
            );
        }

        using var generators = profile.Measure("loading.generators." + project.Name);
        var generated = RunGenerators(
            project.Name,
            driver,
            compilation,
            sources,
            cancellationToken
        );
        var result = new GeneratedProject(
            _contextIds[project.Id],
            signature,
            diagnosticIds,
            metadata,
            generated.Compilation,
            generated.Trees,
            edges.ToArray(),
            analyzers,
            new Lazy<MetadataReference?>(() =>
            {
                return EmitReferenceAssembly(generated.Compilation, cancellationToken);
            })
        );
        _generations?.Store(result);
        return result;
    }

    private async Task<CompilationContext> CaptureProjectAsync(
        Project project,
        Project liveProject,
        Task<GeneratedProject> generation,
        CompilerCapture capture,
        SnapshotLoadOptions options,
        string sdkVersion,
        ConcurrentQueue<string> failures,
        PipelineProfile profile,
        CancellationToken cancellationToken
    )
    {
        var generated = await generation;
        using var capturing = profile.Measure("loading.capture." + project.Name);
        var metadata = generated.Metadata;
        foreach (var path in metadata.InputPaths)
            _inputs.TryAdd(path, 0);
        foreach (var directory in metadata.InputDirectories)
            _inputDirectories.TryAdd(directory, 0);
        ThrowLoadFailures(failures);
        await CompilationValidation.ValidateAsync(
            generated.Compilation,
            liveProject,
            generated.Analyzers,
            options.ValidateCompilation,
            cancellationToken
        );
        var snapshot = WithMetadata(
            capture.Capture(
                project.Name,
                project.FilePath!,
                generated.ContextId,
                generated.Compilation,
                generated.Trees,
                generated.DiagnosticIds,
                cancellationToken
            ),
            metadata,
            generated.Edges,
            options,
            sdkVersion
        );
        return new CompilationContext(snapshot, generated.Compilation);
    }

    private static ProjectSnapshot WithMetadata(
        ProjectSnapshot snapshot,
        EvaluatedMetadata metadata,
        CompilationReferenceSnapshot[] edges,
        SnapshotLoadOptions options,
        string sdkVersion
    ) =>
        snapshot with
        {
            CompilationReferences = edges,
            ReferencedContextIds = edges.Select(edge => edge.ContextId).ToArray(),
            TargetFramework = metadata.TargetFramework,
            IsTestProject = metadata.IsTestProject,
            Properties = metadata.Properties,
            BuildProperties = new(options.Properties, StringComparer.OrdinalIgnoreCase),
            SdkVersion = sdkVersion,
            Packages = metadata.Packages,
            SourceRoots = metadata.SourceRoots,
        };

    private static GeneratorDriver? CreateGeneratorDriver(
        Project project,
        ISourceGenerator[] generators
    )
    {
        return generators.Length == 0
            ? null
            : CSharpGeneratorDriver.Create(
                generators,
                AdditionalFilesInBuildOrder(project),
                (CSharpParseOptions)project.ParseOptions!,
                project.AnalyzerOptions.AnalyzerConfigOptionsProvider
            );
    }

    private static GeneratedCompilation RunGenerators(
        string projectName,
        GeneratorDriver? driver,
        CSharpCompilation compilation,
        List<SourceReference> sources,
        CancellationToken cancellationToken
    )
    {
        if (driver is null)
        {
            return new GeneratedCompilation(compilation, []);
        }

        // The compiler gives generators each referenced project as an assembly, whose symbols have no
        // source declarations. A generator that follows a symbol to its declaration, as ASP.NET's
        // validation generator does with a readonly field passed to any Map call, may otherwise reach a
        // syntax tree of another compilation.
        var replaced = new List<(MetadataReference Source, MetadataReference Assembly)>();
        var input = compilation;
        foreach (var source in sources)
        {
            if (source.Assembly.Value?.WithProperties(source.Reference.Properties) is { } assembly)
            {
                input = input.ReplaceReference(source.Reference, assembly);
                replaced.Add((source.Reference, assembly));
            }
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(
            input,
            out var generatedCompilation,
            out var diagnostics,
            cancellationToken
        );
        var results = driver.GetRunResult();
        var failure = results.Results.FirstOrDefault(result => result.Exception is not null);
        if (
            failure.Exception is not null
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        )
        {
            throw new InvalidOperationException(
                $"Source generation failed in '{projectName}': {failure.Exception?.Message ?? string.Join("; ", diagnostics)}"
            );
        }

        var output = (CSharpCompilation)generatedCompilation;
        foreach (var (source, assembly) in replaced)
        {
            output = output.ReplaceReference(assembly, source);
        }

        return new GeneratedCompilation(output, results.GeneratedTrees.ToHashSet());
    }

    // Emits what a reference assembly contains. A project whose declarations do not compile has none,
    // and its dependents' generators see its source instead.
    private static MetadataReference? EmitReferenceAssembly(
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        using var image = new MemoryStream();
        var result = compilation.Emit(
            image,
            options: new EmitOptions(metadataOnly: true, includePrivateMembers: false),
            cancellationToken: cancellationToken
        );
        return result.Success ? MetadataReference.CreateFromImage(image.ToArray()) : null;
    }

    // The workspace lists additional files by random document ID; the compiler receives them in
    // MSBuild item order, which decides the order of generator outputs such as Razor's.
    private static ImmutableArray<AdditionalText> AdditionalFilesInBuildOrder(Project project)
    {
        var order = new Dictionary<string, int>();
        foreach (var document in project.AdditionalDocuments)
        {
            if (document.FilePath is { } path)
            {
                order.TryAdd(path, order.Count);
            }
        }

        return
        [
            .. project.AnalyzerOptions.AdditionalFiles.OrderBy(file =>
                order.GetValueOrDefault(file.Path, int.MaxValue)
            ),
        ];
    }

    private static async Task<string[]> GetDiagnosticIdsAsync(
        Project project,
        CancellationToken cancellationToken
    )
    {
        var ids = new HashSet<string>(project.CompilationOptions!.SpecificDiagnosticOptions.Keys);
        foreach (var document in project.AnalyzerConfigDocuments)
        {
            var text = await document.GetTextAsync(cancellationToken);
            foreach (
                Match match in Regex.Matches(
                    text.ToString(),
                    @"dotnet_diagnostic\.(CS\d+)\.severity",
                    RegexOptions.CultureInvariant
                )
            )
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids.ToArray();
    }

    private static void ThrowLoadFailures(ConcurrentQueue<string> failures)
    {
        if (!failures.IsEmpty)
        {
            throw new InvalidOperationException(
                $"MSBuild loading failed: {string.Join("; ", failures)}. Ensure the target SDK is installed and run dotnet restore first."
            );
        }
    }
}
