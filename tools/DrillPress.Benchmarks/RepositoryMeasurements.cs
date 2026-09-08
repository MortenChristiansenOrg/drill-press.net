using System.IO.Abstractions;
using System.Text.Json;
using DrillPress.Manifest;

namespace DrillPress.Benchmarks;

public sealed class RepositoryMeasurements(IFileSystem fileSystem, string repositoryRoot, string outputDirectory)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly MeasuredProcess _process = new(fileSystem);
    private readonly ProfileMeasurements _profiles = new(fileSystem);
    private readonly List<RepositoryOperation> _operations = [];
    private readonly string _root = repositoryRoot;
    private readonly string _output = outputDirectory;

    public IReadOnlyList<RepositoryOperation> Operations => _operations;

    public async Task<MeasuredExecution> RunAsync(string name, string executable, string[] arguments,
        string workingDirectory, bool findingsAllowed, string[] profileComponents, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"performance: {name}");
        var execution = await _process.RunAsync(executable, arguments, workingDirectory, _fileSystem.Path.Combine(_output, name), cancellationToken);
        _operations.Add(new(name, execution, []));
        await SaveAsync();
        if (execution.ExitCode != 0 && !(findingsAllowed && execution.ExitCode == 1))
        {
            throw new InvalidOperationException($"{name} exited {execution.ExitCode}; see {execution.StandardErrorPath} and {execution.StandardOutputPath}.");
        }

        if (profileComponents.Length > 0)
        {
            _operations[^1] = new(name, execution, _profiles.Read(execution.StandardErrorPath, execution.Measurement.WallMilliseconds, profileComponents));
            await SaveAsync();
        }

        return execution;
    }

    private Task SaveAsync() => _fileSystem.File.WriteAllTextAsync(_fileSystem.Path.Combine(_output, "operations.json"),
        JsonSerializer.Serialize(_operations, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);

    public Task<MeasuredExecution> DotnetAsync(string name, string[] arguments, CancellationToken cancellationToken) =>
        RunAsync(name, "dotnet", arguments, _root, false, [], cancellationToken);
}
