using System.IO.Abstractions;

namespace DrillPress.Engine;

internal sealed class CoverageSession : IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly CoverageProcess _process;
    private readonly CoverageCache _cache;
    private readonly string _directory;
    private readonly Dictionary<string, string> _executed = [];
    private readonly HashSet<string> _restored = [];

    internal CoverageSession(IFileSystem fileSystem, CoverageProcess process, CoverageCache cache)
    {
        _fileSystem = fileSystem;
        _process = process;
        _cache = cache;
        _directory = fileSystem.Path.Combine(cache.Root, Guid.NewGuid().ToString("N"));
        fileSystem.Directory.CreateDirectory(_directory);
    }

    internal async Task RestoreAsync(
        AnalysisProject project,
        CoverageTestRun[] tests,
        CancellationToken cancellationToken
    )
    {
        foreach (var path in tests.Select(test => test.ProjectPath).Distinct())
        {
            var key = path + "\0" + string.Join("\0", CoverageDiscovery.Properties(project));
            if (_restored.Contains(key))
                continue;
            await _process.RunAsync(
                "dotnet",
                ["restore", path, .. CoverageDiscovery.Properties(project)],
                _fileSystem.Path.GetDirectoryName(path)!,
                cancellationToken
            );
            _restored.Add(key);
        }
    }

    internal async Task<string> CollectAsync(
        AnalysisProject project,
        CoverageTestRun[] tests,
        string root,
        CancellationToken cancellationToken
    )
    {
        var tool = await EnsureToolAsync(root, cancellationToken);
        var discovery = new CoverageDiscovery(_fileSystem, _process);
        var reports = new List<string>();
        foreach (var test in tests)
        {
            var runKey =
                test.ProjectPath
                + "\0"
                + test.Framework
                + "\0"
                + string.Join("\0", CoverageDiscovery.Properties(project));
            if (!_executed.TryGetValue(runKey, out var output))
            {
                output = NewReportPath(".coverage");
                await _process.RunAsync(
                    tool,
                    [
                        "collect",
                        "--nologo",
                        "-f",
                        "coverage",
                        "-o",
                        output,
                        "dotnet",
                        "test",
                        .. discovery.TestArguments(test),
                        "--framework",
                        test.Framework,
                        "--no-restore",
                        .. CoverageDiscovery.Properties(project),
                    ],
                    _fileSystem.Path.GetDirectoryName(test.ProjectPath)!,
                    cancellationToken
                );
                _executed.Add(runKey, output);
            }
            reports.Add(output);
        }
        var temporary = NewReportPath();
        // Binary reports retain individual block identities; XML alone loses complementary partial hits.
        await _process.RunAsync(
            tool,
            ["merge", "--nologo", "-f", "xml", "-o", temporary, .. reports],
            root,
            cancellationToken
        );
        return temporary;
    }

    private string NewReportPath(string extension = ".xml") =>
        _fileSystem.Path.Combine(_directory, Guid.NewGuid().ToString("N") + extension);

    private async Task<string> EnsureToolAsync(string root, CancellationToken cancellationToken)
    {
        using var toolLock = await _cache.AcquireToolAsync(cancellationToken);
        var toolDirectory = _fileSystem.Path.Combine(_cache.Root, "tool");
        var tool = _fileSystem.Path.Combine(
            toolDirectory,
            OperatingSystem.IsWindows() ? "dotnet-coverage.exe" : "dotnet-coverage"
        );
        if (!_fileSystem.File.Exists(tool))
            await _process.RunAsync(
                "dotnet",
                [
                    "tool",
                    "install",
                    "dotnet-coverage",
                    "--version",
                    CoverageTool.Version,
                    "--tool-path",
                    toolDirectory,
                ],
                root,
                cancellationToken
            );
        return tool;
    }

    public void Dispose() => _fileSystem.Directory.Delete(_directory, recursive: true);
}
