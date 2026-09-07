using System.IO.Abstractions;
using System.Text.Json;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public static class MemoryProbe
{
    public static async Task<MemorySample> RunWorkerAsync(IFileSystem fileSystem, string planPath, BundleMode mode, string caseName)
    {
        var plan = new BenchmarkPlanFile(fileSystem).Read(planPath);
        var scenario = plan.Cases.Single(item => item.Name == caseName);
        if (OperatingSystem.IsLinux())
        {
            return await LinuxMemoryProbe.RunAsync(fileSystem, plan, mode, scenario);
        }

        long peakMemory = 0;
        var output = await BenchmarkExecution.RunAsync(
            plan, mode, scenario, process => peakMemory = ChildPeakMemory.Read(process));
        return new MemorySample(mode, caseName, peakMemory, output);
    }

    public static async Task<MemorySample[]> CollectAsync(BenchmarkPlan plan, string planPath)
    {
        var samples = new List<MemorySample>();
        foreach (var scenario in plan.Cases)
        {
            foreach (var mode in Enum.GetValues<BundleMode>())
            {
                // No build here: the prebuilt worker launches exactly one target.
                var result = await ProcessRunner.RunAsync("dotnet",
                    [typeof(MemoryProbe).Assembly.Location, "--memory-worker", planPath, mode.ToString(), scenario.Name],
                    plan.RepositoryRoot);
                ProcessRunner.RequireSuccess(result);
                var sample = JsonSerializer.Deserialize<MemorySample>(result.StandardOutput)
                    ?? throw new InvalidOperationException("The memory worker returned no sample.");
                BundleContract.Validate(scenario, sample.Output);
                if (sample.Mode != mode || sample.Case != scenario.Name || sample.PeakMemoryBytes <= 0)
                {
                    throw new InvalidOperationException("The memory worker returned an invalid sample.");
                }

                samples.Add(sample);
            }
        }

        return samples.ToArray();
    }
}
