using System.Diagnostics;
using System.Text.Json;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public static class BenchmarkExecution
{
    public static BenchmarkPlan ReadPlan(string path) =>
        JsonSerializer.Deserialize<BenchmarkPlan>(File.ReadAllText(path))
        ?? throw new InvalidOperationException("The benchmark plan is empty.");

    public static async Task<ProcessOutput> RunAsync(
        BenchmarkPlan plan, BundleMode mode, BundleCase scenario, Action<Process>? afterExit = null)
    {
        var output = mode switch
        {
            BundleMode.Managed => await ProcessRunner.RunAsync(
                "dotnet", [plan.ManagedBundle, .. scenario.Arguments], plan.RepositoryRoot, afterExit: afterExit),
            BundleMode.Native => await ProcessRunner.RunAsync(
                plan.NativeBundle, scenario.Arguments, plan.RepositoryRoot, afterExit: afterExit),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        BundleContract.Validate(scenario, output);
        return output;
    }
}
