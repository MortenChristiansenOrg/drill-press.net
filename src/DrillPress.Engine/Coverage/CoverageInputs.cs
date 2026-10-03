using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace DrillPress.Engine;

internal sealed class CoverageInputs(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal string Root(AnalysisProject project)
    {
        var directory = _fileSystem.Path.GetDirectoryName(
            _fileSystem.Path.GetFullPath(project.ProjectPath)
        )!;
        for (
            var current = directory;
            current is not null;
            current = _fileSystem.Path.GetDirectoryName(current)
        )
            if (
                _fileSystem.Directory.Exists(_fileSystem.Path.Combine(current, ".git"))
                || _fileSystem.File.Exists(_fileSystem.Path.Combine(current, ".git"))
                || _fileSystem
                    .Directory.EnumerateFiles(current)
                    .Any(path =>
                        _fileSystem.Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx"
                    )
            )
                return current;
        return directory;
    }

    internal string[] Files(string root) => Enumerate(root).Order(StringComparer.Ordinal).ToArray();

    private IEnumerable<string> Enumerate(string directory)
    {
        foreach (var file in _fileSystem.Directory.EnumerateFiles(directory))
            yield return file;
        foreach (var child in _fileSystem.Directory.EnumerateDirectories(directory))
        {
            if (
                _fileSystem.Path.GetFileName(child)
                    is ".git"
                        or "artifacts"
                        or "TestResults"
                        or ".vs"
                || (_fileSystem.File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0
            )
                continue;
            foreach (var file in Enumerate(child))
                yield return file;
        }
    }

    internal string Identity(
        string[] files,
        AnalysisProject project,
        bool includeBuild,
        CoveragePlan plan,
        CancellationToken cancellationToken
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append("drillpress-coverage-v4/" + CoverageTool.Version);
        Append(project.ProjectPath);
        Append(project.TargetFramework);
        Append(project.Snapshot.SdkVersion);
        Append(plan.SdkIdentity);
        Append(
            System.Text.Json.JsonSerializer.Serialize(
                project.Snapshot.CompilerOptions,
                DrillPress.Manifest.CompilationSnapshotJsonContext.Default.CompilerOptionsSnapshot
            )
        );
        foreach (var test in plan.Tests)
        {
            Append(test.ProjectPath);
            Append(test.Framework);
        }
        var declaredInputs = plan.InputPaths.ToHashSet();
        foreach (var symbol in project.Snapshot.PreprocessorSymbols.Order(StringComparer.Ordinal))
            Append(symbol);
        foreach (var property in CoverageDiscovery.Properties(project))
            Append(property);
        foreach (
            var variable in Environment
                .GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal)
        )
        {
            Append(variable.Key.ToString()!);
            Append(variable.Value?.ToString() ?? "");
        }
        foreach (
            var file in files
                .Concat(project.Sources.Select(source => source.Document.Path))
                .Concat(project.Snapshot.MetadataReferences)
                .Concat(project.Snapshot.ExternalReferences.Select(reference => reference.Path))
                .Distinct()
                .Order(StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var components = file.Replace('\\', '/').Split('/');
            var build = components.Contains("bin") || components.Contains("obj");
            if (
                build
                && !declaredInputs.Contains(file)
                && (
                    !includeBuild
                    || _fileSystem.Path.GetExtension(file) is not (".dll" or ".pdb" or ".json")
                )
            )
                continue;
            Append(file);
            if (!_fileSystem.File.Exists(file))
            {
                Append("missing");
                continue;
            }
            using var stream = _fileSystem.File.OpenRead(file);
            Append(Convert.ToHexString(SHA256.HashData(stream)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());

        void Append(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\0"));
    }
}
