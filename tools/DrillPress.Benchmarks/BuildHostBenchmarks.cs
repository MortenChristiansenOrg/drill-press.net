using System.IO.Abstractions;
using BenchmarkDotNet.Attributes;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

/// <summary>Exports the restored <see cref="BuildHostWorkload"/> in a fresh BuildHost process per iteration.</summary>
public class BuildHostBenchmarks
{
    public const string BuildHostEnvironmentVariable = "DRILLPRESS_BENCHMARK_BUILD_HOST";
    public const string SolutionEnvironmentVariable = "DRILLPRESS_BENCHMARK_SOLUTION";
    private string _buildHost = "";
    private string _solution = "";
    private string _snapshot = "";
    private string _directory = "";

    [GlobalSetup]
    public void Setup()
    {
        var fileSystem = new FileSystem();
        _buildHost = Required(BuildHostEnvironmentVariable);
        _solution = Required(SolutionEnvironmentVariable);
        _directory = fileSystem.Path.GetDirectoryName(_solution)!;
        _snapshot = fileSystem.Path.Combine(_directory, "snapshot.json");
    }

    [Benchmark]
    public async Task<ProcessOutput> Export()
    {
        var output = await ProcessRunner.RunAsync(
            "dotnet",
            [_buildHost, "export", _solution, _snapshot],
            _directory
        );
        ProcessRunner.RequireSuccess(output);
        return output;
    }

    private static string Required(string variable) =>
        Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException($"{variable} is required.");
}
