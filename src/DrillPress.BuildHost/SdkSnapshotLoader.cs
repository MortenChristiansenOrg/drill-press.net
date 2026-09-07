using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.Build.Evaluation;
using Project = Microsoft.CodeAnalysis.Project;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace DrillPress.BuildHost;

internal sealed class SdkSnapshotLoader(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<SnapshotExport> LoadAsync(string target, SnapshotLoadOptions options, string sdkVersion, CancellationToken cancellationToken)
    {
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
        if (_fileSystem.Path.GetExtension(target).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            await workspace.OpenProjectAsync(target, cancellationToken: cancellationToken);
        }
        else
        {
            await workspace.OpenSolutionAsync(target, cancellationToken: cancellationToken);
        }

        ThrowLoadFailures(failures);
        var original = workspace.CurrentSolution;
        var solution = original;
        foreach (var project in original.Projects)
        {
            solution = solution.WithProjectAnalyzerReferences(project.Id, []);
        }

        var results = new Dictionary<ProjectId, CompilationContext>();
        foreach (var id in solution.GetProjectDependencyGraph().GetTopologicallySortedProjects(cancellationToken))
        {
            var project = solution.GetProject(id)!;
            var liveProject = original.GetProject(id)!;
            if (project.Language != LanguageNames.CSharp)
            {
                throw new InvalidOperationException($"Project '{project.Name}' is not C#; its source context cannot be captured.");
            }

            results.Add(id, await CaptureProjectAsync(project, liveProject, solution, results, options, sdkVersion, failures, cancellationToken));
        }

        var contexts = results.Values.OrderBy(context => context.Snapshot.ProjectPath, StringComparer.Ordinal)
            .ThenBy(context => context.Snapshot.TargetFramework, StringComparer.Ordinal).ToArray();
        var envelope = CompilationSnapshot.Create(contexts.Select(context => context.Snapshot).ToArray());
        SnapshotValidation.Validate(envelope);
        return new SnapshotExport(envelope, contexts);
    }

    private async Task<CompilationContext> CaptureProjectAsync(Project project, Project liveProject, Solution solution,
        Dictionary<ProjectId, CompilationContext> completed, SnapshotLoadOptions options, string sdkVersion,
        ConcurrentQueue<string> failures, CancellationToken cancellationToken)
    {
        var metadata = EvaluateMetadata(project, options);
        if (!_fileSystem.File.Exists(metadata.AssetsPath))
        {
            throw new FileNotFoundException($"Restore assets are missing for '{project.FilePath}'. Run dotnet restore for the target first.", metadata.AssetsPath);
        }
    
        var compilation = await project.GetCompilationAsync(cancellationToken) as CSharpCompilation
            ?? throw new InvalidOperationException($"Could not load C# compilation '{project.Name}'.");
        var edges = new List<CompilationReferenceSnapshot>();
        foreach (var reference in project.ProjectReferences)
        {
            if (!completed.TryGetValue(reference.ProjectId, out var dependency))
            {
                throw new InvalidDataException($"Incomplete project graph for '{project.Name}'.");
            }
    
            var originalDependency = await solution.GetProject(reference.ProjectId)!.GetCompilationAsync(cancellationToken);
            var oldReference = compilation.References.OfType<CompilationReference>().SingleOrDefault(item => ReferenceEquals(item.Compilation, originalDependency))
                ?? throw new InvalidDataException($"Missing source compilation edge for '{project.Name}'.");
            compilation = compilation.ReplaceReference(oldReference,
                dependency.Compilation.ToMetadataReference(reference.Aliases, reference.EmbedInteropTypes));
            edges.Add(new CompilationReferenceSnapshot(dependency.Snapshot.ContextId, reference.Aliases.ToArray(), reference.EmbedInteropTypes));
        }
    
        var generated = RunGenerators(liveProject, compilation, cancellationToken);
        compilation = generated.Compilation;
        ThrowLoadFailures(failures);
        CompilationValidation.Validate(compilation, options.ValidateCompilation, cancellationToken);
        var diagnosticIds = await GetDiagnosticIdsAsync(liveProject, cancellationToken);
        var snapshot = new CompilerCapture(_fileSystem).Capture(project.Name, project.FilePath!, project.Id.Id.ToString("N"),
            compilation, generated.Trees, diagnosticIds, cancellationToken) with
        {
            CompilationReferences = edges.ToArray(), ReferencedContextIds = edges.Select(edge => edge.ContextId).ToArray(),
            TargetFramework = metadata.TargetFramework, IsTestProject = metadata.IsTestProject,
            Properties = metadata.Properties, SdkVersion = sdkVersion,
        };
        return new CompilationContext(snapshot, compilation);
    }

    private EvaluatedMetadata EvaluateMetadata(Project project, SnapshotLoadOptions options)
    {
        var properties = new Dictionary<string, string>(options.Properties, StringComparer.OrdinalIgnoreCase);
        using var collection = new ProjectCollection(properties);
        var evaluated = collection.LoadProject(project.FilePath!);
        var global = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        global.TryGetValue("build_property.TargetFramework", out var framework);
        if (string.IsNullOrEmpty(framework) && project.Name.LastIndexOf('(') is >= 0 and var start && project.Name.EndsWith(')'))
        {
            var suffix = project.Name[(start + 1)..^1];
            if (evaluated.GetPropertyValue("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries).Contains(suffix))
            {
                framework = suffix;
            }
        }

        if (!string.IsNullOrEmpty(framework))
        {
            evaluated.SetGlobalProperty("TargetFramework", framework);
            evaluated.ReevaluateIfNecessary();
        }

        framework = evaluated.GetPropertyValue("TargetFramework");
        var test = evaluated.GetProperty("IsTestProject");
        var isTest = test is not null && test.EvaluatedValue.Length > 0
            ? test.EvaluatedValue.Equals("true", StringComparison.OrdinalIgnoreCase)
            : project.Name.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                project.MetadataReferences.Any(reference => reference.Display?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true);
        properties["TargetFramework"] = framework;
        if (test is not null)
        {
            properties["IsTestProject"] = test.EvaluatedValue;
        }

        var assets = evaluated.GetPropertyValue("ProjectAssetsFile");
        if (string.IsNullOrEmpty(assets))
        {
            assets = _fileSystem.Path.Combine(_fileSystem.Path.GetDirectoryName(project.FilePath!)!, "obj", "project.assets.json");
        }
        else
        {
            assets = _fileSystem.Path.GetFullPath(assets, _fileSystem.Path.GetDirectoryName(project.FilePath!)!);
        }

        return new EvaluatedMetadata(framework, isTest, properties, assets);
    }

    private static GeneratedCompilation RunGenerators(Project project, CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        var loadFailures = new List<string>();
        foreach (var reference in project.AnalyzerReferences.OfType<Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference>())
        {
            reference.AnalyzerLoadFailed += (_, args) => loadFailures.Add(args.Message);
        }

        var generators = project.AnalyzerReferences.SelectMany(reference => reference.GetGenerators(LanguageNames.CSharp)).ToArray();
        if (loadFailures.Count > 0)
        {
            throw new InvalidOperationException($"Analyzer loading failed in '{project.Name}': {string.Join("; ", loadFailures)}");
        }

        if (generators.Length == 0)
        {
            return new GeneratedCompilation(compilation, []);
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, project.AnalyzerOptions.AdditionalFiles,
            (CSharpParseOptions)project.ParseOptions!, project.AnalyzerOptions.AnalyzerConfigOptionsProvider);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out var diagnostics, cancellationToken);
        var results = driver.GetRunResult();
        var failure = results.Results.FirstOrDefault(result => result.Exception is not null);
        if (failure.Exception is not null || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException($"Source generation failed in '{project.Name}': {failure.Exception?.Message ?? string.Join("; ", diagnostics)}");
        }

        return new GeneratedCompilation((CSharpCompilation)generatedCompilation, results.GeneratedTrees.ToHashSet());
    }

    private static async Task<string[]> GetDiagnosticIdsAsync(Project project, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(project.CompilationOptions!.SpecificDiagnosticOptions.Keys);
        foreach (var document in project.AnalyzerConfigDocuments)
        {
            var text = await document.GetTextAsync(cancellationToken);
            foreach (Match match in Regex.Matches(text.ToString(), @"dotnet_diagnostic\.(CS\d+)\.severity", RegexOptions.CultureInvariant))
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
            throw new InvalidOperationException($"MSBuild loading failed: {string.Join("; ", failures)}. Ensure the target SDK is installed and run dotnet restore first.");
        }
    }

    private sealed record EvaluatedMetadata(string TargetFramework, bool IsTestProject, Dictionary<string, string> Properties, string AssetsPath);
    private sealed record GeneratedCompilation(CSharpCompilation Compilation, HashSet<SyntaxTree> Trees);
}
