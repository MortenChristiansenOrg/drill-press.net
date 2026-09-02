using Microsoft.Build.Evaluation;

internal sealed class EvaluatedProjectClassifier : IDisposable
{
    private readonly IReadOnlyDictionary<string, string> _workspaceProperties;
    private readonly ProjectCollection _projectCollection;
    private readonly Dictionary<string, bool> _cache;

    public EvaluatedProjectClassifier(IReadOnlyDictionary<string, string> workspaceProperties)
    {
        _workspaceProperties = workspaceProperties;
        _projectCollection = new ProjectCollection(
            workspaceProperties.ToDictionary(static pair => pair.Key, static pair => pair.Value));
        _cache = new Dictionary<string, bool>(GetPathComparer());
    }

    public bool IsTestProject(Microsoft.CodeAnalysis.Project project)
    {
        var path = Path.GetFullPath(project.FilePath
            ?? throw new InvalidOperationException($"Project '{project.Name}' has no path."));
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
            "build_property.TargetFramework",
            out var targetFramework);
        var cacheKey = $"{path}\0{targetFramework}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var evaluationProperties = new Dictionary<string, string>(
            _workspaceProperties,
            StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            evaluationProperties["TargetFramework"] = targetFramework;
        }

        var evaluatedProject = _projectCollection.LoadProject(path, evaluationProperties, toolsVersion: null);
        var evaluatedValue = evaluatedProject.GetPropertyValue("IsTestProject");
        _projectCollection.UnloadProject(evaluatedProject);
        if (!string.IsNullOrWhiteSpace(evaluatedValue))
        {
            if (!bool.TryParse(evaluatedValue, out var isTestProject))
            {
                throw new InvalidOperationException(
                    $"Project '{path}' has invalid evaluated IsTestProject value '{evaluatedValue}'.");
            }

            _cache[cacheKey] = isTestProject;
            return isTestProject;
        }

        var name = project.Name;
        var inferred = name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
                       name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase) ||
                       path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           .Any(segment => segment.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                                           segment.Equals("tests", StringComparison.OrdinalIgnoreCase)) ||
                       project.MetadataReferences.Any(reference =>
                           Path.GetFileNameWithoutExtension(reference.Display ?? string.Empty) is
                               "xunit.core" or "nunit.framework" or
                               "Microsoft.VisualStudio.TestPlatform.TestFramework");
        _cache[cacheKey] = inferred;
        return inferred;
    }

    public void Dispose() => _projectCollection.Dispose();

    private static StringComparer GetPathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
