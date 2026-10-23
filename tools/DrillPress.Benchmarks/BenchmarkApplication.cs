using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public sealed class BenchmarkApplication(IFileSystem fileSystem)
{
    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args is ["--memory-worker", var planPath, var mode, var caseName])
            {
                var sample = await MemoryProbe.RunWorkerAsync(
                    fileSystem,
                    planPath,
                    Enum.Parse<BundleMode>(mode),
                    caseName
                );
                Console.WriteLine(JsonSerializer.Serialize(sample));
                return 0;
            }

            if (args is ["--queries", "--output", var queryOutput])
            {
                MeasureQueries(queryOutput);
                Console.WriteLine($"Reports: {queryOutput}");
                return 0;
            }

            if (args is ["--build-host", "--output", var buildHostOutput])
            {
                await MeasureBuildHostAsync(buildHostOutput);
                Console.WriteLine($"Reports: {buildHostOutput}");
                return 0;
            }

            var output = args switch
            {
                [] => null,
                ["--output", var path] when !string.IsNullOrWhiteSpace(path) => path,
                _ => throw new ArgumentException(
                    "Usage: NativeBundles.cs [--output <new-directory>] | --queries --output <new-directory> | --build-host --output <new-directory>"
                ),
            };
            using var session = await VerificationSession.CreateAsync(fileSystem, output);
            await MeasureAsync(session);
            Console.WriteLine($"Reports: {session.OutputDirectory}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"native-bundles: {error.Message}");
            return 1;
        }
    }

    // Query scaling runs in process; each iteration binds a fresh synthetic solution.
    private void MeasureQueries(string output)
    {
        if (fileSystem.Directory.Exists(output) || fileSystem.File.Exists(output))
            throw new ArgumentException("The query benchmark output must be a new directory.");
        fileSystem.Directory.CreateDirectory(output);
        var job = Job
            .Default.WithId("Queries")
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(5)
            .WithInvocationCount(1)
            .WithUnrollFactor(1);
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(job)
            .AddExporter(JsonExporter.Full)
            .WithArtifactsPath(fileSystem.Path.GetFullPath(output));
        var summary = BenchmarkRunner.Run<QueryBenchmarks>(config);
        if (
            summary.HasCriticalValidationErrors
            || summary.Reports.Length != 6
            || summary.Reports.Any(report => !report.Success || report.ResultStatistics is null)
        )
            throw new InvalidOperationException(
                "BenchmarkDotNet did not complete all six query benchmarks."
            );
    }

    // Measures the Release BuildHost of this checkout from the repository root on a restored workload.
    private async Task MeasureBuildHostAsync(string output)
    {
        if (fileSystem.Directory.Exists(output) || fileSystem.File.Exists(output))
            throw new ArgumentException("The BuildHost benchmark output must be a new directory.");
        var buildHost = fileSystem.Path.GetFullPath(
            "src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll"
        );
        if (!fileSystem.File.Exists(buildHost))
            throw new FileNotFoundException(
                "Build DrillPress.slnx in Release and run from the repository root first.",
                buildHost
            );
        var solution = new BuildHostWorkload(fileSystem).Write(
            fileSystem.Path.GetFullPath(fileSystem.Path.Combine(output, "workload"))
        );
        ProcessRunner.RequireSuccess(
            await ProcessRunner.RunAsync(
                "dotnet",
                ["restore", solution],
                fileSystem.Path.GetDirectoryName(solution)!,
                timeout: TimeSpan.FromMinutes(10)
            )
        );
        var job = Job
            .Default.WithId("BuildHostProcess")
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(5)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithEnvironmentVariable(BuildHostBenchmarks.BuildHostEnvironmentVariable, buildHost)
            .WithEnvironmentVariable(BuildHostBenchmarks.SolutionEnvironmentVariable, solution);
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(job)
            .AddExporter(JsonExporter.Full)
            .WithArtifactsPath(
                fileSystem.Path.GetFullPath(fileSystem.Path.Combine(output, "BenchmarkDotNet"))
            );
        var summary = BenchmarkRunner.Run<BuildHostBenchmarks>(config);
        if (
            summary.HasCriticalValidationErrors
            || summary.Reports.Length != 1
            || summary.Reports.Any(report => !report.Success || report.ResultStatistics is null)
        )
            throw new InvalidOperationException(
                "BenchmarkDotNet did not complete the BuildHost benchmark."
            );
    }

    private async Task MeasureAsync(VerificationSession session)
    {
        var startup = new BundleCase(
            "startup",
            [],
            BundleOutcome.Failure,
            [],
            Encoding.UTF8.GetBytes(
                "Usage: <rule-bundle> check <snapshot> [--profile] [--no-optimization]"
                    + Environment.NewLine
            )
        );
        var plan = new BenchmarkPlan(
            session.RepositoryRoot,
            session.ManagedBundle,
            session.NativeBundle,
            [startup, .. session.Cases]
        );
        var planPath = fileSystem.Path.Combine(session.OutputDirectory, "benchmark-plan.json");
        await new BenchmarkPlanFile(fileSystem).WriteAsync(planPath, plan);
        var memory = await MemoryProbe.CollectAsync(plan, planPath);
        await MeasurementReport.WriteAsync(fileSystem, session, memory);

        var job = Job
            .Default.WithId("BundleProcess")
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(5)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithEnvironmentVariable(BundleBenchmarks.PlanEnvironmentVariable, planPath);
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(job)
            .AddExporter(JsonExporter.Full)
            .WithArtifactsPath(fileSystem.Path.Combine(session.OutputDirectory, "BenchmarkDotNet"));
        var summary = BenchmarkRunner.Run<BundleBenchmarks>(config);
        if (
            summary.HasCriticalValidationErrors
            || summary.Reports.Length != 8
            || summary.Reports.Any(report => !report.Success || report.ResultStatistics is null)
        )
        {
            throw new InvalidOperationException(
                "BenchmarkDotNet did not complete all eight bundle benchmarks."
            );
        }
    }
}
