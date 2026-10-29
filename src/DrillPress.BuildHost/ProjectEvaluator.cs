using System.IO.Abstractions;
using DrillPress.Manifest;
using Microsoft.Build.Evaluation;
using Project = Microsoft.CodeAnalysis.Project;
using ProjectId = Microsoft.CodeAnalysis.ProjectId;

namespace DrillPress.BuildHost;

/// <summary>
/// Evaluates each loaded context's MSBuild policy facts in one shared project collection, so SDK
/// imports are parsed once per export instead of once per context.
/// </summary>
internal sealed class ProjectEvaluator : IAsyncDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IReadOnlyDictionary<string, string> _properties;
    private readonly ProjectCollection _collection;
    private Task _evaluation = Task.CompletedTask;

    public ProjectEvaluator(IFileSystem fileSystem, IReadOnlyDictionary<string, string> properties)
    {
        _fileSystem = fileSystem;
        _properties = properties;
        _collection = new ProjectCollection(
            new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    /// Evaluates the contexts one at a time in the given order on a background thread; the collection
    /// serializes evaluations anyway, and the order lets the first captured contexts start soonest.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, Task<EvaluatedMetadata>> Start(
        IReadOnlyList<Project> projects,
        CancellationToken cancellationToken
    )
    {
        return projects.ToDictionary(
            project => project.Id,
            project => Start(project, cancellationToken)
        );
    }

    internal Task<EvaluatedMetadata> Start(Project project, CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<EvaluatedMetadata>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _evaluation = _evaluation.ContinueWith(
            _ => Complete(project, result, cancellationToken),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );
        return result.Task;
    }

    private void Complete(
        Project project,
        TaskCompletionSource<EvaluatedMetadata> result,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            result.SetCanceled(cancellationToken);
            return;
        }
        try
        {
            result.SetResult(Evaluate(project));
        }
        catch (Exception exception)
        {
            result.SetException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _evaluation;
        _collection.Dispose();
    }

    private EvaluatedMetadata Evaluate(Project project)
    {
        var global = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        global.TryGetValue("build_property.TargetFramework", out var framework);
        if (
            string.IsNullOrEmpty(framework)
            && project.Name.LastIndexOf('(') is >= 0 and var start
            && project.Name.EndsWith(')')
        )
        {
            var suffix = project.Name[(start + 1)..^1];
            if (
                Load(project, null)
                    .GetPropertyValue("TargetFrameworks")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(suffix)
            )
            {
                framework = suffix;
            }
        }

        // One evaluation with the framework as a global property equals evaluating the outer
        // project and then setting that global property.
        var evaluated = Load(project, string.IsNullOrEmpty(framework) ? null : framework);
        return Capture(project, evaluated);
    }

    private EvaluatedMetadata Capture(Project project, Microsoft.Build.Evaluation.Project evaluated)
    {
        var properties = new Dictionary<string, string>(
            _properties,
            StringComparer.OrdinalIgnoreCase
        );
        var framework = evaluated.GetPropertyValue("TargetFramework");
        var test = evaluated.GetProperty("IsTestProject");
        var isTest =
            test is not null && test.EvaluatedValue.Length > 0
                ? test.EvaluatedValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                : project.Name.Contains("test", StringComparison.OrdinalIgnoreCase)
                    || project.MetadataReferences.Any(reference =>
                        reference.Display?.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                        == true
                    );
        properties["TargetFramework"] = framework;
        if (test is not null)
        {
            properties["IsTestProject"] = test.EvaluatedValue;
        }

        var assets = evaluated.GetPropertyValue("ProjectAssetsFile");
        if (string.IsNullOrEmpty(assets))
        {
            assets = _fileSystem.Path.Combine(
                _fileSystem.Path.GetDirectoryName(project.FilePath!)!,
                "obj",
                "project.assets.json"
            );
        }
        else
        {
            assets = _fileSystem.Path.GetFullPath(
                assets,
                _fileSystem.Path.GetDirectoryName(project.FilePath!)!
            );
        }

        foreach (var property in evaluated.AllEvaluatedProperties)
        {
            // Only project policy facts: do not serialize arbitrary environment variables or secrets.
            if (
                property.Name
                is "RootNamespace"
                    or "AssemblyName"
                    or "Nullable"
                    or "OutputType"
                    or "IsPackable"
                    or "LangVersion"
            )
            {
                properties[property.Name] = property.EvaluatedValue;
            }
        }

        var central = evaluated
            .GetItems("PackageVersion")
            .GroupBy(item => item.EvaluatedInclude, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().GetMetadataValue("Version"),
                StringComparer.OrdinalIgnoreCase
            );
        var packages = evaluated
            .GetItems("PackageReference")
            .Select(item =>
            {
                var version = item.GetMetadataValue("VersionOverride");
                if (version.Length == 0)
                {
                    version = item.GetMetadataValue("Version");
                }

                return new PackageReferenceSnapshot(
                    item.EvaluatedInclude,
                    version.Length > 0
                        ? version
                        : central.GetValueOrDefault(item.EvaluatedInclude, "")
                );
            })
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Id, StringComparer.Ordinal)
            .ThenBy(package => package.Version, StringComparer.Ordinal)
            .ToArray();
        var roots = evaluated
            .GetItems("SourceRoot")
            .Select(item => item.EvaluatedInclude)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
        var compiler = new TargetCompiler(_fileSystem).CompilerAssemblyPath(
            _fileSystem.Path.GetDirectoryName(project.FilePath!)!,
            evaluated.GetPropertyValue
        );
        return new EvaluatedMetadata(
            framework,
            isTest,
            properties,
            assets,
            packages,
            roots,
            compiler
        );
    }

    // The collection returns an already loaded project for the same path and global properties.
    private Microsoft.Build.Evaluation.Project Load(Project project, string? framework)
    {
        var properties = new Dictionary<string, string>(
            _properties,
            StringComparer.OrdinalIgnoreCase
        );
        if (framework is not null)
        {
            properties["TargetFramework"] = framework;
        }

        return _collection.LoadProject(project.FilePath!, properties, null);
    }
}
