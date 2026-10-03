using System.IO.Abstractions;
using System.Text.Json;

namespace DrillPress.Engine;

internal sealed class CoverageDiscovery(
    IFileSystem fileSystem,
    CoverageProcess process,
    CoverageEvaluations evaluations
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CoverageProcess _process = process;
    private readonly CoverageEvaluations _evaluations = evaluations;
    private readonly HashSet<string> _inputs = [];
    private readonly HashSet<string> _sdks = [];

    internal async Task<CoveragePlan> PlanAsync(
        string[] projectPaths,
        AnalysisProject target,
        string root,
        CancellationToken cancellationToken
    )
    {
        var tests = new List<CoverageTestRun>();
        var trackedProjects = new HashSet<string>(
            projectPaths,
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : EqualityComparer<string>.Default
        );
        foreach (var path in projectPaths)
        {
            using var json = await EvaluateAsync(path, target, null, root, cancellationToken);
            await TrackExternalReferencesAsync(
                json,
                target,
                root,
                trackedProjects,
                cancellationToken
            );
            var properties = json.RootElement.GetProperty("Properties");
            var frameworks = (properties.GetProperty("TargetFrameworks").GetString() ?? "").Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
            );
            if (frameworks.Length == 0)
                frameworks = [properties.GetProperty("TargetFramework").GetString() ?? ""];
            foreach (var framework in frameworks.Where(framework => framework.Length > 0))
            {
                using var evaluated = await EvaluateAsync(
                    path,
                    target,
                    framework,
                    root,
                    cancellationToken
                );
                await TrackExternalReferencesAsync(
                    evaluated,
                    target,
                    root,
                    trackedProjects,
                    cancellationToken
                );
                var evaluatedProperties = evaluated.RootElement.GetProperty("Properties");
                if (
                    !IsTrue(evaluatedProperties, "IsTestProject")
                    && !IsTrue(evaluatedProperties, "IsTestingPlatformApplication")
                    && (
                        IsFalse(evaluatedProperties, "IsTestProject")
                        || !HasTestPackage(evaluated.RootElement)
                    )
                )
                    continue;
                if (
                    await ReachesAsync(
                        path,
                        evaluated,
                        _fileSystem.Path.GetFullPath(target.ProjectPath),
                        target,
                        framework,
                        root,
                        new HashSet<string>(
                            OperatingSystem.IsWindows()
                                ? StringComparer.OrdinalIgnoreCase
                                : EqualityComparer<string>.Default
                        ),
                        cancellationToken
                    )
                )
                    tests.Add(new(path, framework));
            }
        }
        return new(
            tests.ToArray(),
            _inputs.Order(StringComparer.Ordinal).ToArray(),
            string.Join(";", _sdks.Order(StringComparer.Ordinal))
        );
    }

    private async Task TrackExternalReferencesAsync(
        JsonDocument evaluated,
        AnalysisProject target,
        string root,
        HashSet<string> trackedProjects,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var reference in evaluated
                .RootElement.GetProperty("Items")
                .GetProperty("ProjectReference")
                .EnumerateArray()
        )
        {
            var path = _fileSystem.Path.GetFullPath(reference.GetProperty("FullPath").GetString()!);
            if (!trackedProjects.Add(path))
                continue;
            using var dependency = await EvaluateAsync(path, target, null, root, cancellationToken);
            await TrackExternalReferencesAsync(
                dependency,
                target,
                root,
                trackedProjects,
                cancellationToken
            );
            var properties = dependency.RootElement.GetProperty("Properties");
            foreach (
                var framework in (
                    properties.GetProperty("TargetFrameworks").GetString() ?? ""
                ).Split(';', StringSplitOptions.RemoveEmptyEntries)
            )
            {
                using var context = await EvaluateAsync(
                    path,
                    target,
                    framework,
                    root,
                    cancellationToken
                );
                await TrackExternalReferencesAsync(
                    context,
                    target,
                    root,
                    trackedProjects,
                    cancellationToken
                );
            }
        }
    }

    private static bool HasTestPackage(JsonElement project) =>
        project.GetProperty("Items").TryGetProperty("PackageReference", out var packages)
        && packages
            .EnumerateArray()
            .Any(package =>
                new[]
                {
                    "Microsoft.NET.Test.Sdk",
                    "xunit.v3",
                    "xunit.v3.mtp-v2",
                    "MSTest",
                    "NUnit",
                }.Contains(
                    package.GetProperty("Identity").GetString(),
                    StringComparer.OrdinalIgnoreCase
                )
            );

    private static bool IsTrue(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var value)
        && value.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsFalse(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var value)
        && value.GetString()?.Equals("false", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<bool> ReachesAsync(
        string path,
        JsonDocument evaluated,
        string targetPath,
        AnalysisProject target,
        string framework,
        string root,
        HashSet<string> visited,
        CancellationToken cancellationToken
    )
    {
        if (SamePath(path, targetPath))
            return true;
        if (!visited.Add(path))
            return false;
        foreach (
            var reference in evaluated
                .RootElement.GetProperty("Items")
                .GetProperty("ProjectReference")
                .EnumerateArray()
        )
        {
            var referencePath = _fileSystem.Path.GetFullPath(
                reference.GetProperty("FullPath").GetString()!
            );
            if (SamePath(referencePath, targetPath))
                return true;
            using var dependency = await EvaluateAsync(
                referencePath,
                target,
                framework,
                root,
                cancellationToken
            );
            if (
                await ReachesAsync(
                    referencePath,
                    dependency,
                    targetPath,
                    target,
                    framework,
                    root,
                    visited,
                    cancellationToken
                )
            )
                return true;
        }
        return false;
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(
            first,
            second,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private async Task<JsonDocument> EvaluateAsync(
        string path,
        AnalysisProject target,
        string? framework,
        string root,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "msbuild",
            path,
            "-getProperty:IsTestProject,IsTestingPlatformApplication,TargetFramework,TargetFrameworks,MSBuildAllProjects,NETCoreSdkVersion",
            "-getItem:ProjectReference,PackageReference,Reference,Compile,Content,None,AdditionalFiles,EmbeddedResource,Analyzer",
        };
        arguments.AddRange(Properties(target));
        if (framework is not null)
            arguments.Add("-p:TargetFramework=" + framework);
        var directory = _fileSystem.Path.GetDirectoryName(path)!;
        var json = JsonDocument.Parse(
            await _evaluations.GetAsync(
                directory + "\0" + string.Join("\0", arguments),
                () => _process.RunAsync("dotnet", arguments, directory, cancellationToken)
            )
        );
        RecordInputs(path, json.RootElement);
        return json;
    }

    private void RecordInputs(string projectPath, JsonElement project)
    {
        _inputs.Add(projectPath);
        var properties = project.GetProperty("Properties");
        if (properties.TryGetProperty("NETCoreSdkVersion", out var sdk))
            _sdks.Add(sdk.GetString() ?? "");
        if (properties.TryGetProperty("MSBuildAllProjects", out var imports))
            foreach (
                var path in (imports.GetString() ?? "").Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
                _inputs.Add(
                    _fileSystem.Path.GetFullPath(
                        path,
                        _fileSystem.Path.GetDirectoryName(projectPath)!
                    )
                );
        foreach (
            var itemGroup in project
                .GetProperty("Items")
                .EnumerateObject()
                .Where(group => group.Name != "PackageReference")
        )
        foreach (var item in itemGroup.Value.EnumerateArray())
        {
            if (
                item.TryGetProperty("FullPath", out var fullPath)
                && fullPath.GetString() is { Length: > 0 } path
            )
                _inputs.Add(_fileSystem.Path.GetFullPath(path));
            if (
                item.TryGetProperty("HintPath", out var hint)
                && hint.GetString() is { Length: > 0 } hintPath
            )
                _inputs.Add(
                    _fileSystem.Path.GetFullPath(
                        hintPath,
                        _fileSystem.Path.GetDirectoryName(projectPath)!
                    )
                );
        }
        for (
            var directory = _fileSystem.Path.GetDirectoryName(projectPath);
            directory is not null;
            directory = _fileSystem.Path.GetDirectoryName(directory)
        )
            foreach (
                var name in new[]
                {
                    "Directory.Build.props",
                    "Directory.Build.targets",
                    "Directory.Packages.props",
                    "global.json",
                    "NuGet.Config",
                }
            )
                _inputs.Add(_fileSystem.Path.Combine(directory, name));
    }

    internal string[] TestArguments(CoverageTestRun test)
    {
        var directory = _fileSystem.Path.GetDirectoryName(test.ProjectPath);
        while (directory is not null)
        {
            var global = _fileSystem.Path.Combine(directory, "global.json");
            if (_fileSystem.File.Exists(global))
            {
                using var json = JsonDocument.Parse(_fileSystem.File.ReadAllText(global));
                if (
                    json.RootElement.TryGetProperty("test", out var config)
                    && config.TryGetProperty("runner", out var runner)
                    && runner.GetString() == "Microsoft.Testing.Platform"
                )
                    return ["--project", test.ProjectPath];
                break;
            }
            directory = _fileSystem.Path.GetDirectoryName(directory);
        }
        return [test.ProjectPath];
    }

    internal static string[] Properties(AnalysisProject project) =>
        project
            .Snapshot.BuildProperties.Where(property =>
                !property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => "-p:" + property.Key + "=" + property.Value)
            .ToArray();
}
