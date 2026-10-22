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
using Microsoft.CodeAnalysis.MSBuild;
using Project = Microsoft.CodeAnalysis.Project;

namespace DrillPress.BuildHost;

internal sealed class SdkSnapshotLoader(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

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

        using var workspace = MSBuildWorkspace.Create(options.Properties);
        workspace.LoadMetadataForReferencedProjects = false;
        var failures = new ConcurrentQueue<string>();
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                failures.Enqueue(args.Diagnostic.Message);
            }
        });
        using (profile.Measure("loading.workspace"))
        {
            if (
                _fileSystem
                    .Path.GetExtension(target)
                    .Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            )
            {
                await workspace.OpenProjectAsync(target, cancellationToken: cancellationToken);
            }
            else
            {
                await workspace.OpenSolutionAsync(target, cancellationToken: cancellationToken);
            }
        }

        ThrowLoadFailures(failures);
        var original = workspace.CurrentSolution;
        var solution = original;
        foreach (var project in original.Projects)
        {
            solution = solution.WithProjectAnalyzerReferences(project.Id, []);
        }

        CompilationContext[] captured;
        using (profile.Measure("loading.contexts"))
        {
            captured = await CaptureProjectsAsync(
                original,
                solution,
                options,
                sdkVersion,
                failures,
                cancellationToken
            );
        }

        var projectOnly =
            !options.IncludeReferencedProjects
            && _fileSystem
                .Path.GetExtension(target)
                .Equals(".csproj", StringComparison.OrdinalIgnoreCase);
        var contexts = captured
            .Select(context => new CompilationContext(
                context.Snapshot with
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
                context.Compilation
            ))
            .OrderBy(context => context.Snapshot.ProjectPath, StringComparer.Ordinal)
            .ThenBy(context => context.Snapshot.TargetFramework, StringComparer.Ordinal)
            .ToArray();
        var envelope = CompilationSnapshot.Create(
            contexts.Select(context => context.Snapshot).ToArray()
        );
        SnapshotValidation.Validate(envelope);
        return new SnapshotExport(envelope, contexts);
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
        CancellationToken cancellationToken
    )
    {
        var order = solution
            .GetProjectDependencyGraph()
            .GetTopologicallySortedProjects(cancellationToken)
            .Select(id => solution.GetProject(id)!)
            .ToArray();
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
                        solution,
                        evaluation,
                        dependencies,
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
        Solution solution,
        Task<EvaluatedMetadata> evaluation,
        Dictionary<ProjectId, Task<GeneratedProject>?> dependencies,
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
        var compiling = project.GetCompilationAsync(cancellationToken);
        await Task.WhenAny(Task.WhenAll(evaluation, compiling));
        var metadata = await evaluation;
        if (!_fileSystem.File.Exists(metadata.AssetsPath))
        {
            throw new FileNotFoundException(
                $"Restore assets are missing for '{project.FilePath}'. Run dotnet restore for the target first.",
                metadata.AssetsPath
            );
        }

        var compilation =
            await compiling as CSharpCompilation
            ?? throw new InvalidOperationException(
                $"Could not load C# compilation '{project.Name}'."
            );
        var edges = new List<CompilationReferenceSnapshot>();
        foreach (var reference in project.ProjectReferences)
        {
            if (dependencies[reference.ProjectId] is not { } pending)
            {
                throw new InvalidDataException($"Incomplete project graph for '{project.Name}'.");
            }

            var dependency = await pending;
            var originalDependency = await solution
                .GetProject(reference.ProjectId)!
                .GetCompilationAsync(cancellationToken);
            var oldReference =
                compilation
                    .References.OfType<CompilationReference>()
                    .SingleOrDefault(item => ReferenceEquals(item.Compilation, originalDependency))
                ?? throw new InvalidDataException(
                    $"Missing source compilation edge for '{project.Name}'."
                );
            compilation = compilation.ReplaceReference(
                oldReference,
                dependency.Compilation.ToMetadataReference(
                    reference.Aliases,
                    reference.EmbedInteropTypes
                )
            );
            edges.Add(
                new CompilationReferenceSnapshot(
                    ContextId(reference.ProjectId),
                    reference.Aliases.ToArray(),
                    reference.EmbedInteropTypes
                )
            );
        }

        var analyzers = new TargetCompiler(_fileSystem).Select(
            project.Name,
            liveProject.AnalyzerReferences,
            metadata.CompilerAssemblyPath
        );
        var generated = RunGenerators(liveProject, analyzers, compilation, cancellationToken);
        return new GeneratedProject(
            metadata,
            generated.Compilation,
            generated.Trees,
            edges.ToArray(),
            analyzers
        );
    }

    private async Task<CompilationContext> CaptureProjectAsync(
        Project project,
        Project liveProject,
        Task<GeneratedProject> generation,
        CompilerCapture capture,
        SnapshotLoadOptions options,
        string sdkVersion,
        ConcurrentQueue<string> failures,
        CancellationToken cancellationToken
    )
    {
        var generated = await generation;
        var metadata = generated.Metadata;
        ThrowLoadFailures(failures);
        await CompilationValidation.ValidateAsync(
            generated.Compilation,
            liveProject,
            generated.Analyzers,
            options.ValidateCompilation,
            cancellationToken
        );
        var diagnosticIds = await GetDiagnosticIdsAsync(liveProject, cancellationToken);
        var snapshot = capture.Capture(
            project.Name,
            project.FilePath!,
            ContextId(project.Id),
            generated.Compilation,
            generated.Trees,
            diagnosticIds,
            cancellationToken
        ) with
        {
            CompilationReferences = generated.Edges,
            ReferencedContextIds = generated.Edges.Select(edge => edge.ContextId).ToArray(),
            TargetFramework = metadata.TargetFramework,
            IsTestProject = metadata.IsTestProject,
            Properties = metadata.Properties,
            BuildProperties = new(options.Properties, StringComparer.OrdinalIgnoreCase),
            SdkVersion = sdkVersion,
            Packages = metadata.Packages,
            SourceRoots = metadata.SourceRoots,
        };
        return new CompilationContext(snapshot, generated.Compilation);
    }

    private static string ContextId(ProjectId id) => id.Id.ToString("N");

    private static GeneratedCompilation RunGenerators(
        Project project,
        ProjectAnalyzers analyzers,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        var generators = analyzers.Load(reference => reference.GetGenerators(LanguageNames.CSharp));
        if (generators.Length == 0)
        {
            return new GeneratedCompilation(compilation, []);
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators,
            project.AnalyzerOptions.AdditionalFiles,
            (CSharpParseOptions)project.ParseOptions!,
            project.AnalyzerOptions.AnalyzerConfigOptionsProvider
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
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
                $"Source generation failed in '{project.Name}': {failure.Exception?.Message ?? string.Join("; ", diagnostics)}"
            );
        }

        return new GeneratedCompilation(
            (CSharpCompilation)generatedCompilation,
            results.GeneratedTrees.ToHashSet()
        );
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

    private sealed record GeneratedProject(
        EvaluatedMetadata Metadata,
        CSharpCompilation Compilation,
        HashSet<SyntaxTree> Trees,
        CompilationReferenceSnapshot[] Edges,
        ProjectAnalyzers Analyzers
    );

    private sealed record GeneratedCompilation(
        CSharpCompilation Compilation,
        HashSet<SyntaxTree> Trees
    );
}
